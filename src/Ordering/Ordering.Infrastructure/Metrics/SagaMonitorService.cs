using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Infrastructure.Persistence;

namespace NetCommerce.Ordering.Infrastructure.Metrics;

/// <summary>
///     Background agent that performs periodic 'Point-in-Time' snapshots of the Saga table.
///     This ensures metrics are 100% accurate even after a system reboot.
/// </summary>
/// <remarks>
///     <para>
///         We poll the database rather than incrementing counters in Saga handlers because:
///         <list type="bullet">
///             <item>Counter drift is avoided if a process crashes mid-transaction</item>
///             <item>Metrics stay accurate after application restarts</item>
///             <item>Horizontal scaling doesn't cause double-counting</item>
///         </list>
///     </para>
///     <para>
///         The 15-second interval balances real-time visibility with database efficiency.
///     </para>
/// </remarks>
public sealed class SagaMonitorService(
    IServiceScopeFactory scopeFactory,
    OrderingMetrics metrics,
    ILogger<SagaMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "SagaMonitorService started. Polling saga state every {Interval} seconds",
            PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(PollInterval);

        // Initial snapshot on startup
        await SafeUpdateMetrics(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeUpdateMetrics(stoppingToken);
        }
    }

    private async Task SafeUpdateMetrics(CancellationToken ct)
    {
        try
        {
            await UpdateSagaMetrics(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown - no need to log
        }
        catch (Exception ex)
        {
            // Never allow a metrics failure to crash the background worker.
            // Just log and retry on next tick.
            logger.LogWarning(
                ex,
                "Failed to update saga metrics. Will retry in {Interval} seconds",
                PollInterval.TotalSeconds);
        }
    }

    private async Task UpdateSagaMetrics(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        // Use AsNoTracking and only select what's needed for efficiency.
        // Wolverine stores sagas in the wolverine.wolverine_saga table,
        // but we query via EF Core which maps OrderFulfillmentSaga.
        var stats = await db.Set<OrderFulfillmentSaga>()
            .AsNoTracking()
            .GroupBy(x => x.State)
            .Select(g => new { State = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        // Update the metrics singleton (thread-safe via Interlocked)
        metrics.ReservingInventoryCount = stats
            .FirstOrDefault(x => x.State == OrderFulfillmentState.ReservingInventory)?.Count ?? 0;

        metrics.ProcessingPaymentCount = stats
            .FirstOrDefault(x => x.State == OrderFulfillmentState.ProcessingPayment)?.Count ?? 0;

        metrics.ConfirmingInventoryCount = stats
            .FirstOrDefault(x => x.State == OrderFulfillmentState.ConfirmingInventory)?.Count ?? 0;

        logger.LogDebug(
            "Saga metrics updated: Reserving={Reserving}, Paying={Paying}, Confirming={Confirming}",
            metrics.ReservingInventoryCount,
            metrics.ProcessingPaymentCount,
            metrics.ConfirmingInventoryCount);
    }
}
