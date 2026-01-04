using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.Persistence;

namespace NetCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
///     Background service that periodically cleans up expired stock reservations.
///     Runs on a configurable interval to release reservations where ExpiresAt &lt; Now and Status == Active.
///     Uses TimeProvider for deterministic time operations and testability.
/// </summary>
public class ReservationCleanupJob : BackgroundService
{
    private readonly ILogger<ReservationCleanupJob> _logger;
    private readonly ReservationCleanupOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public ReservationCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationCleanupJob> logger,
        IOptions<ReservationCleanupOptions> options,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("ReservationCleanupJob is disabled");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(_options.IntervalMs);

        _logger.LogInformation(
            "ReservationCleanupJob started. Interval: {Interval}ms, BatchSize: {BatchSize}",
            _options.IntervalMs, _options.BatchSize);

        try
        {
            await CleanupExpiredReservationsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during reservation cleanup");
        }

        // Use TimeProvider for PeriodicTimer to enable deterministic testing
        using var timer = new PeriodicTimer(interval, _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                await CleanupExpiredReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during reservation cleanup");
            }

        _logger.LogInformation("ReservationCleanupJob stopped");
    }

    private async Task CleanupExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Find expired active reservations (limited by BatchSize)
        var expiredReservations = await context.StockReservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Take(_options.BatchSize)
            .Select(r => new { r.Id, r.StockId, r.OrderId, r.Quantity })
            .ToListAsync(cancellationToken);

        var stuckPayments = await context.StockReservations
            .Where(r => r.Status == ReservationStatus.PendingPayment)
            .Where(r => r.UpdatedAt <= now.AddHours(-2))
            .OrderBy(r => r.UpdatedAt)
            .Take(_options.BatchSize)
            .Select(r => new { r.Id, r.StockId, r.OrderId, r.Quantity })
            .ToListAsync(cancellationToken);

        if (expiredReservations.Count == 0 && stuckPayments.Count == 0) return;

        var stockIds = expiredReservations
            .Select(r => r.StockId)
            .Concat(stuckPayments.Select(r => r.StockId))
            .Distinct()
            .ToList();

        var stocks = await context.Stocks
            .Include(s => s.Reservations)
            .Where(s => stockIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        var totalReleased = 0;

        foreach (var stock in stocks)
        {
            var reservationsForStock = expiredReservations
                .Where(r => r.StockId == stock.Id)
                .ToList();

            var stuckForStock = stuckPayments
                .Where(r => r.StockId == stock.Id)
                .ToList();

            foreach (var reservation in reservationsForStock)
            {
                stock.ReleaseReservation(reservation.Id);
                totalReleased++;

                _logger.LogDebug(
                    "Released expired reservation {ReservationId} for Stock {StockId}, OrderId: {OrderId}, Quantity: {Quantity}",
                    reservation.Id, stock.Id, reservation.OrderId, reservation.Quantity);
            }

            foreach (var reservation in stuckForStock)
            {
                stock.ReleaseReservation(reservation.Id);
                totalReleased++;

                _logger.LogWarning(
                    "Released stuck pending-payment reservation {ReservationId} for Stock {StockId}, OrderId: {OrderId}, Quantity: {Quantity}",
                    reservation.Id, stock.Id, reservation.OrderId, reservation.Quantity);
            }
        }

        if (totalReleased > 0)
        {
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Cleaned up {Count} expired reservations from {StockCount} stock items",
                totalReleased, stocks.Count);
        }
    }
}
