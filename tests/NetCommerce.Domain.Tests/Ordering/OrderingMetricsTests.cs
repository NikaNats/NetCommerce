using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Ordering.Infrastructure.Metrics;
using Shouldly;

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Unit tests for OrderingMetrics.
///     Tests the metrics registry and gauge observations.
/// </summary>
public class OrderingMetricsTests : IDisposable
{
    private readonly MeterListener _meterListener;
    private readonly ServiceProvider _serviceProvider;
    private readonly OrderingMetrics _metrics;
    private readonly List<(string Name, object? Value, KeyValuePair<string, object?>[] Tags)> _observedMeasurements = [];

    public OrderingMetricsTests()
    {
        // Create a service provider with MeterFactory
        var services = new ServiceCollection();
        services.AddMetrics();
        _serviceProvider = services.BuildServiceProvider();

        var meterFactory = _serviceProvider.GetRequiredService<IMeterFactory>();
        _metrics = new OrderingMetrics(meterFactory);

        // Set up listener to capture measurements
        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == OrderingMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _observedMeasurements.Add((instrument.Name, measurement, tags.ToArray()));
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public void MeterName_ShouldBeCorrect()
    {
        // Assert
        OrderingMetrics.MeterName.ShouldBe("NetCommerce.Ordering");
    }

    [Fact]
    public void Counters_ShouldInitializeToZero()
    {
        // Assert
        _metrics.ReservingInventoryCount.ShouldBe(0);
        _metrics.ProcessingPaymentCount.ShouldBe(0);
        _metrics.ConfirmingInventoryCount.ShouldBe(0);
    }

    [Fact]
    public async Task Counters_ShouldBeThreadSafe()
    {
        // Arrange
        const long expectedValue = 12345L;

        // Act - Simulate concurrent updates
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            _metrics.ReservingInventoryCount = expectedValue;
            _metrics.ProcessingPaymentCount = expectedValue;
            _metrics.ConfirmingInventoryCount = expectedValue;
        }));
        await Task.WhenAll(tasks);

        // Assert
        _metrics.ReservingInventoryCount.ShouldBe(expectedValue);
        _metrics.ProcessingPaymentCount.ShouldBe(expectedValue);
        _metrics.ConfirmingInventoryCount.ShouldBe(expectedValue);
    }

    [Fact]
    public void ObservableGauge_ShouldEmitMeasurementsWithCorrectTags()
    {
        // Arrange - Set up some non-zero counts for verification
        _metrics.ReservingInventoryCount = 5;
        _metrics.InGracePeriodCount = 2;
        _metrics.LockingInventoryCount = 1;
        _metrics.ProcessingPaymentCount = 10;
        _metrics.ConfirmingInventoryCount = 3;
        _metrics.CompensatingCount = 1;
        _metrics.CompletedCount = 50;
        _metrics.FailedCount = 2;
        _metrics.ManualInterventionCount = 0;

        // Act - Trigger observation
        _meterListener.RecordObservableInstruments();

        // Assert - Should have 9 measurements (one for each saga state)
        var sagaMeasurements = _observedMeasurements
            .Where(m => m.Name == "ordering.fulfillment.sagas.active")
            .ToList();

        sagaMeasurements.Count.ShouldBe(9);

        // Verify each state has correct value and tag (using full state names now)
        var reservingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "ReservingInventory"));
        reservingMeasurement.Value.ShouldBe(5L);

        var gracePeriodMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "InGracePeriod"));
        gracePeriodMeasurement.Value.ShouldBe(2L);

        var payingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "ProcessingPayment"));
        payingMeasurement.Value.ShouldBe(10L);

        var confirmingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "ConfirmingInventory"));
        confirmingMeasurement.Value.ShouldBe(3L);

        var completedMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "Completed"));
        completedMeasurement.Value.ShouldBe(50L);
    }

    [Fact]
    public void ObservableGauge_ShouldReflectUpdatedValues()
    {
        // Arrange - Initial values
        _metrics.ReservingInventoryCount = 1;
        _metrics.ProcessingPaymentCount = 2;
        _metrics.ConfirmingInventoryCount = 3;

        _meterListener.RecordObservableInstruments();
        _observedMeasurements.Clear();

        // Act - Update values
        _metrics.ReservingInventoryCount = 100;
        _metrics.ProcessingPaymentCount = 200;
        _metrics.ConfirmingInventoryCount = 300;

        _meterListener.RecordObservableInstruments();

        // Assert - New observation should reflect updated values (using full state names)
        var sagaMeasurements = _observedMeasurements
            .Where(m => m.Name == "ordering.fulfillment.sagas.active")
            .ToList();

        var reservingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "ReservingInventory"));
        reservingMeasurement.Value.ShouldBe(100L);

        var payingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "ProcessingPayment"));
        payingMeasurement.Value.ShouldBe(200L);

        var confirmingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "ConfirmingInventory"));
        confirmingMeasurement.Value.ShouldBe(300L);
    }

    [Fact]
    public void Counters_ShouldHandleZeroValues()
    {
        // Arrange - All saga state counters should default to zero
        _metrics.ReservingInventoryCount = 0;
        _metrics.InGracePeriodCount = 0;
        _metrics.LockingInventoryCount = 0;
        _metrics.ProcessingPaymentCount = 0;
        _metrics.ConfirmingInventoryCount = 0;
        _metrics.CompensatingCount = 0;
        _metrics.CompletedCount = 0;
        _metrics.FailedCount = 0;
        _metrics.ManualInterventionCount = 0;

        // Act
        _meterListener.RecordObservableInstruments();

        // Assert - Should have 9 measurements (one for each saga state)
        var sagaMeasurements = _observedMeasurements
            .Where(m => m.Name == "ordering.fulfillment.sagas.active")
            .ToList();

        sagaMeasurements.Count.ShouldBe(9);
        sagaMeasurements.All(m => (long)m.Value! == 0).ShouldBeTrue();
    }
}
