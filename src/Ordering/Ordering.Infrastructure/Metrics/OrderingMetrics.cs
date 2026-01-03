using System.Diagnostics.Metrics;

namespace NetCommerce.Ordering.Infrastructure.Metrics;

/// <summary>
///     High-performance metrics registry for the Ordering Module.
///     Provides real-time snapshots of the OrderFulfillmentSaga business process health.
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

    // Internal counters updated by the SagaMonitorService
    private long _reservingInventoryCount;
    private long _processingPaymentCount;
    private long _confirmingInventoryCount;

    public OrderingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        // We create a single gauge with 'Tags' to distinguish between states.
        // This is better for Grafana/Aspire visualization than 3 separate gauges.
        // The callback is invoked when metrics are scraped (pull model).
        meter.CreateObservableGauge(
            name: "ordering.fulfillment.sagas.active",
            observeValues: ObserveSagaCounts,
            unit: "{sagas}",
            description: "Current count of active order fulfillment processes grouped by state");
    }

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
