using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
/// Background service that polls the outbox_messages table and publishes domain events.
/// This ensures guaranteed delivery of domain events after the transaction commits.
/// </summary>
public class OutboxProcessor<TDbContext> : BackgroundService 
    where TDbContext : DbContext, IOutboxDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor<TDbContext>> _logger;
    private readonly OutboxProcessorOptions _options;
    private readonly string _contextName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("OutboxProcessor for {ContextName} is disabled", _contextName);
            return;
        }

        _logger.LogInformation(
            "OutboxProcessor for {ContextName} started. Polling every {Interval}ms, batch size: {BatchSize}",
            _contextName, _options.PollingIntervalMs, _options.BatchSize);

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

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOn == null && m.RetryCount < _options.MaxRetries)
            .OrderBy(m => m.OccurredOn)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Processing {Count} outbox messages for {ContextName}", messages.Count, _contextName);

        foreach (var message in messages)
        {
            try
            {
                var domainEvent = DeserializeDomainEvent(message);
                
                if (domainEvent is null)
                {
                    _logger.LogWarning(
                        "Could not deserialize outbox message {MessageId} of type {Type}",
                        message.Id, message.Type);
                    
                    message.MarkAsFailed("Failed to deserialize event");
                    continue;
                }

                await mediator.Publish(domainEvent, cancellationToken);
                
                message.MarkAsProcessed();
                
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
                
                message.MarkAsFailed(ex.Message);
            }
        }

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
