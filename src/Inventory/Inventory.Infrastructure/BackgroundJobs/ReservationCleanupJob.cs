#nullable enable
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
///     Health-aware with circuit breaker: after 3 consecutive failures it marks
///     CleanupJobHealthState.IsDegraded = true, causing /health/ready to fail
///     and K8s to remove the pod from rotation. Exponential backoff prevents
///     log spam and DB hammering. Uses TimeProvider for deterministic testing.
/// </summary>
public class ReservationCleanupJob : BackgroundService
{
    private readonly ILogger<ReservationCleanupJob> _logger;
    private readonly ReservationCleanupOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly CleanupJobHealthState _healthState;
    private int _consecutiveFailures;
    private const int MaxFailuresBeforeDegraded = 3;

    public ReservationCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationCleanupJob> logger,
        IOptions<ReservationCleanupOptions> options,
        TimeProvider? timeProvider = null,
        CleanupJobHealthState? healthState = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _healthState = healthState ?? new CleanupJobHealthState();
    }

    // Test-visible for assertions
    internal int ConsecutiveFailures => _consecutiveFailures;
    internal CleanupJobHealthState HealthState => _healthState;

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

        // Initial cleanup attempt (immediate)
        try
        {
            await CleanupExpiredReservationsAsync(stoppingToken);
            _consecutiveFailures = 0;
            _healthState.IsDegraded = false;
            _healthState.ConsecutiveFailures = 0;
            _healthState.LastSuccessUtc = _timeProvider.GetUtcNow();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            HandleFailure(ex);
            // Exponential backoff before entering periodic loop if initial fails
            if (_healthState.IsDegraded)
            {
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(_consecutiveFailures, 7)));
                backoff = backoff > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : backoff;
                try { await Task.Delay(backoff, _timeProvider, stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }

        using var timer = new PeriodicTimer(interval, _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupExpiredReservationsAsync(stoppingToken);
                _consecutiveFailures = 0;
                _healthState.IsDegraded = false;
                _healthState.ConsecutiveFailures = 0;
                _healthState.LastSuccessUtc = _timeProvider.GetUtcNow();
                _healthState.LastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                HandleFailure(ex);
                if (_healthState.IsDegraded)
                {
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(_consecutiveFailures, 7)));
                    if (backoff > TimeSpan.FromMinutes(5)) backoff = TimeSpan.FromMinutes(5);
                    _logger.LogWarning("Cleanup job backing off for {Backoff}s before next attempt", backoff.TotalSeconds);
                    try { await Task.Delay(backoff, _timeProvider, stoppingToken); } catch (OperationCanceledException) { break; }
                }
            }
        }

        _logger.LogInformation("ReservationCleanupJob stopped");
    }

    private void HandleFailure(Exception ex)
    {
        _consecutiveFailures++;
        _healthState.ConsecutiveFailures = _consecutiveFailures;
        _healthState.LastError = ex.Message;
        _logger.LogError(ex, "Cleanup cycle failed. Consecutive failures: {Count}", _consecutiveFailures);

        if (_consecutiveFailures >= MaxFailuresBeforeDegraded)
        {
            _healthState.IsDegraded = true;
            _logger.LogCritical("Cleanup job entering degraded state. ConsecutiveFailures={Count}, LastError={Error}. Pod will fail readiness probes.", _consecutiveFailures, ex.Message);
        }
    }

    private async Task CleanupExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var now = _timeProvider.GetUtcNow().UtcDateTime;

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
