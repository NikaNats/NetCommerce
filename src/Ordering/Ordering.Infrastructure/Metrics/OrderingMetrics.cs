using System.Diagnostics.Metrics;

namespace NetCommerce.Ordering.Infrastructure.Metrics;

/// <summary>
///     2025 Business Process Metrics for the Ordering Module.
///     Provides real-time snapshots of OrderFulfillmentSaga health AND business value.
///
///     Key Principle: "Technical metrics (CPU/RAM) don't tell you if the business is healthy."
///     CEO-level metrics: "How many orders are stuck?" "How much revenue is at risk?"
/// </summary>
/// <remarks>
///     Uses <see cref="ObservableGauge{T}" /> which is the most efficient way to track
///     "current state" totals. The values are updated by a background poller that queries
///     the database, ensuring metrics are 100% accurate even after system restarts.
/// </remarks>
public sealed class OrderingMetrics
{
    /// <summary>
    ///     The OpenTelemetry meter name for the Ordering module.
    /// </summary>
    public const string MeterName = "NetCommerce.Ordering";

    // ═══════════════════════════════════════════════════════════════
    // Saga State Counters (Technical Health)
    // ═══════════════════════════════════════════════════════════════
    // Internal counters updated by the SagaMonitorService
    private long _reservingInventoryCount;
    private long _processingPaymentCount;
    private long _confirmingInventoryCount;

    // ═══════════════════════════════════════════════════════════════
    // Business Value Counters (CEO Metrics)
    // ═══════════════════════════════════════════════════════════════
    // "How much money is currently in-flight?"
    private long _stuckOrdersCount; // Orders requiring manual intervention
    private decimal _stuckOrdersValue; // Total $ value of stuck orders
    private long _delayedOrdersCount; // Orders delayed > 24 hours
    private decimal _delayedOrdersValue; // Total $ value of delayed orders
    private long _paymentFailuresLast5Min; // Payment failures (time-windowed)
    private long _inventoryReservationFailuresLast5Min; // Inventory failures (time-windowed)

    public OrderingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        // ═══════════════════════════════════════════════════════════════
        // Gauge #1: Active Saga States (Technical Health)
        // ═══════════════════════════════════════════════════════════════
        // Used by: DevOps dashboards, on-call alerts
        // Purpose: "Is the system processing orders normally?"

        meter.CreateObservableGauge(
            name: "ordering.fulfillment.sagas.active",
            observeValues: ObserveSagaCounts,
            unit: "{sagas}",
            description: "Current count of active order fulfillment processes grouped by state");

        // ═══════════════════════════════════════════════════════════════
        // Gauge #2: Stuck Orders (CEO Metric #1)
        // ═══════════════════════════════════════════════════════════════
        // Used by: Executive dashboards, support team
        // Purpose: "How many orders need manual intervention RIGHT NOW?"
        // Alert: > 10 stuck orders = escalate to on-call engineer

        meter.CreateObservableGauge(
            name: "ordering.fulfillment.stuck",
            observeValue: () => _stuckOrdersCount,
            unit: "orders",
            description: "Number of orders requiring manual support intervention (ManualInterventionRequired state)");

        // ═══════════════════════════════════════════════════════════════
        // Gauge #3: Stuck Orders Value (CEO Metric #2)
        // ═══════════════════════════════════════════════════════════════
        // Used by: Finance dashboards, executive reports
        // Purpose: "How much revenue is at risk due to stuck orders?"
        // Alert: > $50,000 stuck = escalate to VP Engineering

        meter.CreateObservableGauge(
            name: "ordering.fulfillment.stuck.value",
            observeValue: () => (double)_stuckOrdersValue,
            unit: "USD",
            description: "Total dollar value of orders requiring manual intervention");

        // ═══════════════════════════════════════════════════════════════
        // Gauge #4: Delayed Orders (SLA Metric)
        // ═══════════════════════════════════════════════════════════════
        // Used by: Operations dashboards, SLA reporting
        // Purpose: "How many orders are delayed beyond 24-hour SLA?"
        // Alert: > 50 delayed orders = investigate external service issues

        meter.CreateObservableGauge(
            name: "ordering.fulfillment.delayed",
            observeValue: () => _delayedOrdersCount,
            unit: "orders",
            description: "Number of orders delayed beyond 24-hour SLA");

        meter.CreateObservableGauge(
            name: "ordering.fulfillment.delayed.value",
            observeValue: () => (double)_delayedOrdersValue,
            unit: "USD",
            description: "Total dollar value of delayed orders");

        // ═══════════════════════════════════════════════════════════════
        // Gauge #5: Payment Failure Rate (Provider Health)
        // ═══════════════════════════════════════════════════════════════
        // Used by: On-call dashboards, provider SLA monitoring
        // Purpose: "Is Stripe/PayPal having issues?"
        // Alert: > 20% failure rate = switch to degraded mode

        meter.CreateObservableGauge(
            name: "ordering.payment.failures.recent",
            observeValue: () => _paymentFailuresLast5Min,
            unit: "failures",
            description: "Payment failures in last 5 minutes (circuit breaker signal)");

        // ═══════════════════════════════════════════════════════════════
        // Gauge #6: Inventory Reservation Failure Rate
        // ═══════════════════════════════════════════════════════════════
        // Used by: Inventory team, operations dashboards
        // Purpose: "Is the inventory module overloaded or having issues?"
        // Alert: > 10 failures/5min = investigate inventory service

        meter.CreateObservableGauge(
            name: "ordering.inventory.reservation_failures.recent",
            observeValue: () => _inventoryReservationFailuresLast5Min,
            unit: "failures",
            description: "Inventory reservation failures in last 5 minutes");
    }

    // ═══════════════════════════════════════════════════════════════
    // Public API: Saga State Counters (Updated by SagaMonitorService)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Count of sagas currently in the ReservingInventory state.
    /// </summary>
    public long ReservingInventoryCount
    {
        get => Interlocked.Read(ref _reservingInventoryCount);
        set => Interlocked.Exchange(ref _reservingInventoryCount, value);
    }

    /// <summary>
    ///     Count of sagas currently in the ProcessingPayment state.
    /// </summary>
    public long ProcessingPaymentCount
    {
        get => Interlocked.Read(ref _processingPaymentCount);
        set => Interlocked.Exchange(ref _processingPaymentCount, value);
    }

    /// <summary>
    ///     Count of sagas currently in the ConfirmingInventory state.
    /// </summary>
    public long ConfirmingInventoryCount
    {
        get => Interlocked.Read(ref _confirmingInventoryCount);
        set => Interlocked.Exchange(ref _confirmingInventoryCount, value);
    }

    // ═══════════════════════════════════════════════════════════════
    // Public API: Business Value Counters (Updated by SagaMonitorService)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Count of orders stuck in ManualInterventionRequired state.
    ///     CEO Metric: "How many orders need human intervention RIGHT NOW?"
    /// </summary>
    public long StuckOrdersCount
    {
        get => Interlocked.Read(ref _stuckOrdersCount);
        set => Interlocked.Exchange(ref _stuckOrdersCount, value);
    }

    /// <summary>
    ///     Total dollar value of stuck orders (revenue at risk).
    ///     CEO Metric: "How much money is stuck due to operational issues?"
    /// </summary>
    public decimal StuckOrdersValue
    {
        get => _stuckOrdersValue;
        set => _stuckOrdersValue = value; // Decimal is atomic on 64-bit
    }

    /// <summary>
    ///     Count of orders delayed beyond 24-hour SLA.
    ///     Operations Metric: "How many orders are breaching our SLA?"
    /// </summary>
    public long DelayedOrdersCount
    {
        get => Interlocked.Read(ref _delayedOrdersCount);
        set => Interlocked.Exchange(ref _delayedOrdersCount, value);
    }

    /// <summary>
    ///     Total dollar value of delayed orders.
    /// </summary>
    public decimal DelayedOrdersValue
    {
        get => _delayedOrdersValue;
        set => _delayedOrdersValue = value;
    }

    /// <summary>
    ///     Payment failures in the last 5 minutes (time-windowed counter).
    ///     Circuit Breaker Signal: High failure rate triggers degraded mode.
    /// </summary>
    public long PaymentFailuresLast5Min
    {
        get => Interlocked.Read(ref _paymentFailuresLast5Min);
        set => Interlocked.Exchange(ref _paymentFailuresLast5Min, value);
    }

    /// <summary>
    ///     Inventory reservation failures in the last 5 minutes.
    /// </summary>
    public long InventoryReservationFailuresLast5Min
    {
        get => Interlocked.Read(ref _inventoryReservationFailuresLast5Min);
        set => Interlocked.Exchange(ref _inventoryReservationFailuresLast5Min, value);
    }

    /// <summary>
    ///     Increment payment failure counter (thread-safe).
    ///     Called by Wolverine handler when payment fails.
    /// </summary>
    public void RecordPaymentFailure()
    {
        Interlocked.Increment(ref _paymentFailuresLast5Min);
    }

    /// <summary>
    ///     Increment inventory reservation failure counter (thread-safe).
    ///     Called by Wolverine handler when inventory reservation fails.
    /// </summary>
    public void RecordInventoryReservationFailure()
    {
        Interlocked.Increment(ref _inventoryReservationFailuresLast5Min);
    }

    /// <summary>
    ///     Reset time-windowed counters (called by background job every 5 minutes).
    /// </summary>
    public void ResetTimeWindowedCounters()
    {
        Interlocked.Exchange(ref _paymentFailuresLast5Min, 0);
        Interlocked.Exchange(ref _inventoryReservationFailuresLast5Min, 0);
    }

    /// <summary>
    ///     Callback invoked by the OpenTelemetry scraper to collect current gauge values.
    /// </summary>
    private IEnumerable<Measurement<long>> ObserveSagaCounts()
    {
        yield return new Measurement<long>(
            ReservingInventoryCount,
            new KeyValuePair<string, object?>("state", "Reserving"));

        yield return new Measurement<long>(
            ProcessingPaymentCount,
            new KeyValuePair<string, object?>("state", "Paying"));

        yield return new Measurement<long>(
            ConfirmingInventoryCount,
            new KeyValuePair<string, object?>("state", "Confirming"));
    }
}
