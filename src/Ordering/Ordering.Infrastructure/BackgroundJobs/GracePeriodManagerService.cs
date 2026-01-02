using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;

namespace NetCommerce.Ordering.Infrastructure.BackgroundJobs;

/// <summary>
///     Background service that processes orders whose grace period has expired.
///     Runs periodically to find orders in Submitted status that have exceeded
///     the grace period duration and transitions them to AwaitingValidation.
/// </summary>
public sealed class GracePeriodManagerService : BackgroundService
{
    private readonly ILogger<GracePeriodManagerService> _logger;
    private readonly GracePeriodOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public GracePeriodManagerService(
        IServiceScopeFactory scopeFactory,
        ILogger<GracePeriodManagerService> logger,
        IOptions<GracePeriodOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("GracePeriodManagerService is disabled via configuration.");
            return;
        }

        _logger.LogInformation(
            "GracePeriodManagerService started. Grace period: {GracePeriodMinutes} minutes, Check interval: {CheckIntervalSeconds} seconds",
            _options.GracePeriodMinutes,
            _options.CheckIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CheckIntervalSeconds));

        // Initial delay to allow the application to fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessGracePeriodOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing grace period orders. Will retry on next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("GracePeriodManagerService stopped.");
    }

    private async Task ProcessGracePeriodOrdersAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var graceThreshold = DateTime.UtcNow.AddMinutes(-_options.GracePeriodMinutes);

        // Query orders that have been in Submitted status longer than the grace period
        // This query utilizes the IX_Orders_Status_CreatedAt composite index for performance
        var ordersToProcess = await context.Orders
            .Where(o => o.Status == OrderStatus.Submitted && o.CreatedAt < graceThreshold)
            .OrderBy(o => o.CreatedAt) // Process oldest first (FIFO)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (ordersToProcess.Count == 0)
        {
            _logger.LogDebug("No orders ready for grace period confirmation.");
            return;
        }

        _logger.LogInformation(
            "Found {Count} orders ready for grace period confirmation.",
            ordersToProcess.Count);

        var processedCount = 0;
        var errorCount = 0;

        foreach (var order in ordersToProcess)
            try
            {
                order.ConfirmGracePeriod();
                processedCount++;

                _logger.LogInformation(
                    "Grace period confirmed for Order {OrderId} (OrderNumber: {OrderNumber}). Status changed to AwaitingValidation.",
                    order.Id,
                    order.OrderNumber);
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(
                    ex,
                    "Failed to confirm grace period for Order {OrderId}.",
                    order.Id);
            }

        // Save all changes in a single transaction
        // This also dispatches domain events via the outbox pattern
        if (processedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Grace period batch completed. Processed: {ProcessedCount}, Errors: {ErrorCount}",
                processedCount,
                errorCount);
        }
    }
}