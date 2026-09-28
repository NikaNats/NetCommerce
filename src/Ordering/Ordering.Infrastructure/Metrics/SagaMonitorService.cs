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

        // Saga state lives in Wolverine's own store table, not in the EF model
        // (see WolverineSagaStateReader): count live sagas per state in memory.
        // The live-saga table stays small (completed sagas are deleted).
        var sagas = await WolverineSagaStateReader.QuerySagasAsync(db, cancellationToken: ct);

        long Count(OrderFulfillmentState state) => sagas.LongCount(s => s.State == state);

        // Update the metrics singleton (thread-safe via Interlocked)
        // Active states
        metrics.ReservingInventoryCount = Count(OrderFulfillmentState.ReservingInventory);

        metrics.InGracePeriodCount = Count(OrderFulfillmentState.InGracePeriod);

        metrics.LockingInventoryCount = Count(OrderFulfillmentState.LockingInventory);

        metrics.ProcessingPaymentCount = Count(OrderFulfillmentState.ProcessingPayment);

        metrics.ConfirmingInventoryCount = Count(OrderFulfillmentState.ConfirmingInventory);

        metrics.CompensatingCount = Count(OrderFulfillmentState.Compensating);

        // Terminal states
        metrics.CompletedCount = Count(OrderFulfillmentState.Completed);

        metrics.FailedCount = Count(OrderFulfillmentState.Failed);

        // The "nightmare" state
        metrics.ManualInterventionCount = Count(OrderFulfillmentState.ManualInterventionRequired);

        // Also update StuckOrdersCount to match ManualInterventionRequired for backwards compatibility
        metrics.StuckOrdersCount = metrics.ManualInterventionCount;

        logger.LogDebug(
            "Saga metrics updated: Reserving={Reserving}, GracePeriod={Grace}, Locking={Locking}, " +
            "Paying={Paying}, Confirming={Confirming}, Compensating={Compensating}, " +
            "Completed={Completed}, Failed={Failed}, ManualIntervention={Manual}",
            metrics.ReservingInventoryCount,
            metrics.InGracePeriodCount,
            metrics.LockingInventoryCount,
            metrics.ProcessingPaymentCount,
            metrics.ConfirmingInventoryCount,
            metrics.CompensatingCount,
            metrics.CompletedCount,
            metrics.FailedCount,
            metrics.ManualInterventionCount);
    }
}
