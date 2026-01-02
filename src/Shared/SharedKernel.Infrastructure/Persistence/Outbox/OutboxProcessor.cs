using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
///     Background service that polls the outbox_messages table and publishes domain events.
///     This ensures guaranteed delivery of domain events after the transaction commits.
///     Uses SELECT FOR UPDATE SKIP LOCKED to prevent race conditions when multiple workers
///     process messages concurrently.
/// </summary>
public class OutboxProcessor<TDbContext> : BackgroundService
    where TDbContext : DbContext, IOutboxDbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _contextName;
    private readonly ILogger<OutboxProcessor<TDbContext>> _logger;
    private readonly OutboxProcessorOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor<TDbContext>> logger,
        IOptions<OutboxProcessorOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _contextName = typeof(TDbContext).Name;
    }

    /// <summary>
    ///     Gets the raw SQL query that atomically claims messages for processing using FOR UPDATE SKIP LOCKED.
    ///     This prevents race conditions when multiple workers poll the same database:
    ///     - FOR UPDATE: Locks the selected rows within the transaction
    ///     - SKIP LOCKED: Skips rows already locked by other workers instead of waiting
    ///     The query selects messages that are either:
    ///     1. In Pending status and haven't exceeded max retries, OR
    ///     2. Stuck in Processing status for longer than the timeout (abandoned by crashed workers)
    /// </summary>
    private static string GetClaimMessagesSql(string? schema)
    {
        var tableName = string.IsNullOrEmpty(schema) ? "outbox_messages" : $"{schema}.outbox_messages";
        return
            @$"SELECT id, type, content, occurred_on, processed_on, error, retry_count, status, processing_started_at, event_id
FROM {tableName}
WHERE 
    (status = {{0}} AND retry_count < {{1}})
    OR (status = {{2}} AND processing_started_at < {{3}})
ORDER BY occurred_on
LIMIT {{4}}
FOR UPDATE SKIP LOCKED";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("OutboxProcessor for {ContextName} is disabled", _contextName);
            return;
        }

        _logger.LogInformation(
            "OutboxProcessor for {ContextName} started. Polling every {Interval}ms, batch size: {BatchSize}, stuck timeout: {StuckTimeout}s",
            _contextName, _options.PollingIntervalMs, _options.BatchSize, _options.StuckMessageTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages for {ContextName}", _contextName);
            }

            await Task.Delay(_options.PollingIntervalMs, stoppingToken);
        }

        _logger.LogInformation("OutboxProcessor for {ContextName} stopped", _contextName);
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var deadLetterHandler = scope.ServiceProvider.GetService<IOutboxDeadLetterHandler<TDbContext>>();
        var integrationEventLogService = scope.ServiceProvider.GetService<IIntegrationEventLogService>();

        // Use an explicit transaction to ensure FOR UPDATE SKIP LOCKED works correctly
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var stuckThreshold = DateTime.UtcNow.AddSeconds(-_options.StuckMessageTimeoutSeconds);

            // Get the schema from the OutboxMessage entity type configuration
            var schema = dbContext.Model.FindEntityType(typeof(OutboxMessage))?.GetSchema();
            var claimSql = GetClaimMessagesSql(schema);

            // Use raw SQL with FOR UPDATE SKIP LOCKED to atomically claim messages
            // This prevents race conditions when multiple workers poll simultaneously
            var messages = await dbContext.OutboxMessages
                .FromSqlRaw(
                    claimSql,
                    (int)OutboxMessageStatus.Pending,
                    _options.MaxRetries,
                    (int)OutboxMessageStatus.Processing,
                    stuckThreshold,
                    _options.BatchSize)
                .ToListAsync(cancellationToken);

            if (messages.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            _logger.LogDebug("Claimed {Count} outbox messages for {ContextName}", messages.Count, _contextName);

            // Mark all claimed messages as Processing
            foreach (var message in messages) message.ClaimForProcessing();

            // Save the Processing status before starting to publish events
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Process messages outside the transaction to avoid long-running transactions
            await ProcessClaimedMessagesAsync(dbContext, mediator, deadLetterHandler, integrationEventLogService,
                messages, cancellationToken);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task ProcessClaimedMessagesAsync(
        TDbContext dbContext,
        IMediator mediator,
        IOutboxDeadLetterHandler<TDbContext>? deadLetterHandler,
        IIntegrationEventLogService? integrationEventLogService,
        List<OutboxMessage> messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
            try
            {
                var domainEvent = DeserializeDomainEvent(message);

                if (domainEvent is null)
                {
                    _logger.LogWarning(
                        "Could not deserialize outbox message {MessageId} of type {Type}",
                        message.Id, message.Type);

                    message.MarkAsFailed("Failed to deserialize event", _options.MaxRetries);
                    continue;
                }

                await mediator.Publish(domainEvent, cancellationToken);

                message.MarkAsProcessed();

                // Mark the integration event log as published
                if (integrationEventLogService != null)
                    await integrationEventLogService.MarkEventAsPublishedAsync(message.EventId, cancellationToken);

                _logger.LogDebug(
                    "Successfully processed outbox message {MessageId} of type {Type}",
                    message.Id, message.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process outbox message {MessageId} of type {Type}. Retry {Retry}/{MaxRetries}",
                    message.Id, message.Type, message.RetryCount + 1, _options.MaxRetries);

                var domainEvent = DeserializeDomainEvent(message);
                message.MarkAsFailed(ex.Message, _options.MaxRetries);

                // Mark the integration event log as failed when max retries exceeded
                if (message.Status == OutboxMessageStatus.Failed)
                {
                    if (integrationEventLogService != null)
                        await integrationEventLogService.MarkEventAsFailedAsync(message.EventId, ex.Message,
                            cancellationToken);

                    if (deadLetterHandler is not null)
                        try
                        {
                            await deadLetterHandler.HandleAsync(message, domainEvent, ex, cancellationToken);
                        }
                        catch (Exception handlerEx)
                        {
                            _logger.LogCritical(
                                handlerEx,
                                "Dead-letter handler failed for outbox message {MessageId} of type {Type}",
                                message.Id,
                                message.Type);
                        }
                }
            }

        // Save final status changes (Processed or Failed)
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IDomainEvent? DeserializeDomainEvent(OutboxMessage message)
    {
        var eventType = Type.GetType(message.Type);

        if (eventType is null)
        {
            _logger.LogWarning("Could not resolve type {Type} for outbox message {MessageId}",
                message.Type, message.Id);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(message.Content, eventType, JsonOptions) as IDomainEvent;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize outbox message {MessageId} content", message.Id);
            return null;
        }
    }
}