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
    public void Counters_ShouldBeThreadSafe()
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
        Task.WaitAll(tasks.ToArray());

        // Assert
        _metrics.ReservingInventoryCount.ShouldBe(expectedValue);
        _metrics.ProcessingPaymentCount.ShouldBe(expectedValue);
        _metrics.ConfirmingInventoryCount.ShouldBe(expectedValue);
    }

    [Fact]
    public void ObservableGauge_ShouldEmitMeasurementsWithCorrectTags()
    {
        // Arrange
        _metrics.ReservingInventoryCount = 5;
        _metrics.ProcessingPaymentCount = 10;
        _metrics.ConfirmingInventoryCount = 3;

        // Act - Trigger observation
        _meterListener.RecordObservableInstruments();

        // Assert - Should have 3 measurements with different tags
        var sagaMeasurements = _observedMeasurements
            .Where(m => m.Name == "ordering.fulfillment.sagas.active")
            .ToList();

        sagaMeasurements.Count.ShouldBe(3);

        // Verify each state has correct value and tag
        var reservingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "Reserving"));
        reservingMeasurement.Value.ShouldBe(5L);

        var payingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "Paying"));
        payingMeasurement.Value.ShouldBe(10L);

        var confirmingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "Confirming"));
        confirmingMeasurement.Value.ShouldBe(3L);
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

        // Assert - New observation should reflect updated values
        var sagaMeasurements = _observedMeasurements
            .Where(m => m.Name == "ordering.fulfillment.sagas.active")
            .ToList();

        var reservingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "Reserving"));
        reservingMeasurement.Value.ShouldBe(100L);

        var payingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "Paying"));
        payingMeasurement.Value.ShouldBe(200L);

        var confirmingMeasurement = sagaMeasurements
            .FirstOrDefault(m => m.Tags.Any(t => t.Key == "state" && t.Value?.ToString() == "Confirming"));
        confirmingMeasurement.Value.ShouldBe(300L);
    }

    [Fact]
    public void Counters_ShouldHandleZeroValues()
    {
        // Arrange
        _metrics.ReservingInventoryCount = 0;
        _metrics.ProcessingPaymentCount = 0;
        _metrics.ConfirmingInventoryCount = 0;

        // Act
        _meterListener.RecordObservableInstruments();

        // Assert
        var sagaMeasurements = _observedMeasurements
            .Where(m => m.Name == "ordering.fulfillment.sagas.active")
            .ToList();

        sagaMeasurements.Count.ShouldBe(3);
        sagaMeasurements.All(m => (long)m.Value! == 0).ShouldBeTrue();
    }
}
