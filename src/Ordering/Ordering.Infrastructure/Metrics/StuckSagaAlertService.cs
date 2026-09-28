#nullable enable
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Infrastructure.Persistence;

namespace NetCommerce.Ordering.Infrastructure.Metrics;

/// <summary>
///     Information about a single stuck saga, used for alert payloads.
/// </summary>
public sealed record StuckSagaInfo(
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string? FailureReason,
    DateTime StuckSince);

/// <summary>
///     Periodic sweep for sagas parked in <see cref="OrderFulfillmentState.ManualInterventionRequired"/>.
///     A saga lands there only when money was captured but could not be refunded
///     automatically — every new occurrence pages the on-call (PagerDuty) and
///     always emits a CRITICAL log for the SIEM path. Repeats for the same order
///     are suppressed via PagerDuty dedup keys plus an in-memory alerted set.
/// </summary>
public sealed class StuckSagaAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StuckSagaAlertOptions _options;
    private readonly ILogger<StuckSagaAlertService> _logger;

    private readonly HashSet<Guid> _alertedOrderIds = [];

    public StuckSagaAlertService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<StuckSagaAlertOptions> options,
        ILogger<StuckSagaAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("StuckSagaAlertService is disabled via configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(_options.CheckIntervalSeconds, 30));

        _logger.LogInformation(
            "StuckSagaAlertService started. Sweeping stuck sagas every {Interval} seconds",
            interval.TotalSeconds);

        if (string.IsNullOrWhiteSpace(_options.PagerDutyRoutingKey))
        {
            _logger.LogWarning(
                "Ordering:Alerting:PagerDutyRoutingKey is not configured. Stuck sagas will be " +
                "logged at CRITICAL level only — nobody will be paged. Set the routing key.");
        }

        using var timer = new PeriodicTimer(interval);

        // Sweep immediately on startup so a deploy never delays an active page.
        await SafeSweepAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeSweepAsync(stoppingToken);
        }
    }

    private async Task SafeSweepAsync(CancellationToken ct)
    {
        try
        {
            await SweepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            // Alerting must never crash the host; retry on next tick.
            _logger.LogWarning(ex, "Stuck-saga sweep failed. Will retry on next interval.");
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var stuck = (await WolverineSagaStateReader.QuerySagasAsync(
                db,
                OrderFulfillmentState.ManualInterventionRequired,
                cancellationToken: ct))
            .OrderBy(s => s.StartedAt)
            .Select(s => new StuckSagaInfo(
                s.OrderId,
                s.OrderNumber,
                s.TotalAmount.Amount,
                s.TotalAmount.Currency,
                s.FailureReason,
                s.StartedAt))
            .ToList();

        // Forget resolved orders so a recurrence pages again.
        _alertedOrderIds.IntersectWith(stuck.Select(s => s.OrderId));

        foreach (var saga in stuck)
        {
            if (!_alertedOrderIds.Add(saga.OrderId))
                continue;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["alert_type"] = "STUCK_SAGA",
                ["order_id"] = saga.OrderId,
                ["order_number"] = saga.OrderNumber,
                ["severity"] = "CRITICAL"
            }))
            {
                _logger.LogCritical(
                    "STUCK SAGA: Order {OrderId} ({OrderNumber}) requires manual intervention. " +
                    "Amount: {Amount} {Currency}. Reason: {Reason}. Stuck since: {StuckSince:O}. " +
                    "Recover via POST /api/admin/orders/{{id}}/force-complete (or force-cancel + DLQ replay).",
                    saga.OrderId,
                    saga.OrderNumber,
                    saga.Amount,
                    saga.Currency,
                    saga.FailureReason ?? "unknown",
                    saga.StuckSince);
            }

            if (!string.IsNullOrWhiteSpace(_options.PagerDutyRoutingKey))
            {
                var client = _httpClientFactory.CreateClient("PagerDuty");
                await SendPagerDutyAlertAsync(client, _options.PagerDutyRoutingKey, saga, _logger, ct);
            }
        }
    }

    /// <summary>
    ///     Sends a PagerDuty Events API v2 trigger for a stuck saga.
    ///     Returns true when PagerDuty accepted the event. Never throws —
    ///     alert-channel failures must not break the sweep.
    /// </summary>
    public static async Task<bool> SendPagerDutyAlertAsync(
        HttpClient client,
        string routingKey,
        StuckSagaInfo saga,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                routing_key = routingKey,
                event_action = "trigger",
                dedup_key = $"netcommerce-stuck-saga-{saga.OrderId}",
                payload = new
                {
                    summary = $"Stuck saga: order {saga.OrderNumber} charged {saga.Amount} {saga.Currency} but fulfillment stalled ({saga.FailureReason ?? "unknown reason"})",
                    source = "NetCommerce Ordering Module",
                    severity = "critical",
                    timestamp = DateTime.UtcNow.ToString("o"),
                    custom_details = new
                    {
                        order_id = saga.OrderId,
                        order_number = saga.OrderNumber,
                        amount = saga.Amount,
                        currency = saga.Currency,
                        failure_reason = saga.FailureReason,
                        stuck_since = saga.StuckSince.ToString("o")
                    }
                }
            };

            using var response = await client.PostAsJsonAsync("enqueue", payload, ct);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "PagerDuty alert sent for stuck Order {OrderId}", saga.OrderId);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "PagerDuty alert failed for stuck Order {OrderId}: {StatusCode} - {Body}",
                saga.OrderId, response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to send PagerDuty alert for stuck Order {OrderId}. " +
                "CRITICAL log above remains the SIEM fallback.",
                saga.OrderId);
            return false;
        }
    }
}
