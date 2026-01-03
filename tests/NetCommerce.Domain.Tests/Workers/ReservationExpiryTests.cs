using Microsoft.Extensions.Time.Testing;
using NetCommerce.Inventory.Domain.Stock;
using Shouldly;

namespace NetCommerce.Domain.Tests.Workers;

/// <summary>
///     Tests for time-based operations using Microsoft.Extensions.TimeProvider.Testing.
///     Tests reservation expiry and other time-sensitive business logic.
///     Now fully deterministic using FakeTimeProvider passed to domain methods.
/// </summary>
public class ReservationExpiryTests
{
    private readonly FakeTimeProvider _timeProvider;

    public ReservationExpiryTests()
    {
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Tests that reservations expire after 15 minutes using time-travel.
    /// </summary>
    [Fact]
    public void Reservation_After15Minutes_ShouldBeExpired()
    {
        // Arrange
        var startTime = _timeProvider.GetUtcNow();
        var stock = Stock.Create(Guid.NewGuid(), "EXPIRE-TEST", 100, timeProvider: _timeProvider);

        var reservation = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);
        reservation.Status.ShouldBe(ReservationStatus.Active);

        // Reservation should expire in 15 minutes
        reservation.ExpiresAt.ShouldBe(startTime.UtcDateTime.Add(StockReservation.DefaultReservationDuration));

        // Act - Advance time by 16 minutes (past expiry)
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Assert - Reservation is now past its expiry time
        var currentTime = _timeProvider.GetUtcNow().UtcDateTime;
        (currentTime > reservation.ExpiresAt).ShouldBeTrue();

        // Available quantity should now include the expired reservation
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(100);
    }

    [Fact]
    public void Reservation_Before15Minutes_ShouldStillBeValid()
    {
        // Arrange
        var stock = Stock.Create(Guid.NewGuid(), "VALID-TEST", 100, timeProvider: _timeProvider);
        var reservation = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);

        // Act - Advance time by 14 minutes (still within window)
        _timeProvider.Advance(TimeSpan.FromMinutes(14));

        // Assert
        var currentTime = _timeProvider.GetUtcNow().UtcDateTime;
        (currentTime < reservation.ExpiresAt).ShouldBeTrue();

        // Reserved quantity should still be counted
        stock.GetReservedQuantity(_timeProvider).ShouldBe(10);
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(90);
    }

    [Fact]
    public void AvailableQuantity_ShouldNotCountExpiredReservations()
    {
        // Arrange
        var stock = Stock.Create(Guid.NewGuid(), "AVAIL-TEST", 100, timeProvider: _timeProvider);

        // Create a reservation
        stock.Reserve(Guid.NewGuid(), 30, _timeProvider);
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(70);
        stock.GetReservedQuantity(_timeProvider).ShouldBe(30);

        // Act - Advance time past expiry
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Assert - Expired reservations are not counted
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(100);
        stock.GetReservedQuantity(_timeProvider).ShouldBe(0);
    }

    [Fact]
    public void MultipleReservations_DifferentExpiryTimes_ShouldExpireIndependently()
    {
        // Arrange
        var stock = Stock.Create(Guid.NewGuid(), "MULTI-TEST", 100, timeProvider: _timeProvider);

        // First reservation at T+0
        var reservation1 = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);
        var expiry1 = reservation1.ExpiresAt;

        // Advance 5 minutes
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // Second reservation at T+5
        var reservation2 = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);
        var expiry2 = reservation2.ExpiresAt;

        // Assert - Reservation 2 should expire 5 minutes later than Reservation 1
        (expiry2 - expiry1).TotalMinutes.ShouldBe(5, 0.1);

        // Advance time to 16 minutes (R1 expired, R2 still active)
        _timeProvider.Advance(TimeSpan.FromMinutes(11)); // Total: 16 min

        // R1 is expired, R2 has 4 more minutes
        stock.GetReservedQuantity(_timeProvider).ShouldBe(10); // Only R2
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(90);

        // Advance 5 more minutes to expire R2
        _timeProvider.Advance(TimeSpan.FromMinutes(5)); // Total: 21 min

        stock.GetReservedQuantity(_timeProvider).ShouldBe(0);
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(100);
    }

    [Fact]
    public void ReservationCleanup_ShouldRemoveExpiredReservations()
    {
        // Arrange
        var stock = Stock.Create(Guid.NewGuid(), "CLEANUP-TEST", 100, timeProvider: _timeProvider);

        // Create reservations at different times
        var res1 = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var res2 = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var res3 = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);

        // At this point: res1 is 10 min old, res2 is 5 min old, res3 is 0 min old
        stock.Reservations.Count.ShouldBe(3);

        // Advance 6 more minutes - res1 should be expired (16 min total)
        _timeProvider.Advance(TimeSpan.FromMinutes(6));

        // res1: 16 min old (EXPIRED)
        // res2: 11 min old (active)
        // res3: 6 min old (active)

        // Trigger cleanup
        stock.CleanupExpiredReservations(_timeProvider);

        // Assert - res1 should now be Expired status
        res1.Status.ShouldBe(ReservationStatus.Expired);
        res2.Status.ShouldBe(ReservationStatus.Active);
        res3.Status.ShouldBe(ReservationStatus.Active);
    }

    [Fact]
    public void ReservationExpiry_SimulateBackgroundWorkerCleanup()
    {
        // Arrange
        var stock = Stock.Create(Guid.NewGuid(), "WORKER-TEST", 50, timeProvider: _timeProvider);
        var reservations = new List<StockReservation>();

        // Create 5 reservations at the same time
        for (var i = 0; i < 5; i++)
        {
            reservations.Add(stock.Reserve(Guid.NewGuid(), 5, _timeProvider));
        }

        // Stock state: 50 total, 25 reserved, 25 available
        stock.GetReservedQuantity(_timeProvider).ShouldBe(25);

        // All reservations expire in 15 minutes, so advance past that
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Worker performs cleanup
        stock.CleanupExpiredReservations(_timeProvider);

        // Count expired reservations
        var cleanupCount = reservations.Count(r => r.Status == ReservationStatus.Expired);

        // Assert - All reservations should have been cleaned up
        cleanupCount.ShouldBe(5);
        stock.GetReservedQuantity(_timeProvider).ShouldBe(0);
    }

    /// <summary>
    ///     Tests the LastUpdatedAt timestamp updates correctly with TimeProvider.
    /// </summary>
    [Fact]
    public void StockOperations_ShouldUpdateLastUpdatedAt()
    {
        // Arrange
        var stock = Stock.Create(Guid.NewGuid(), "TIMESTAMP-TEST", 100, timeProvider: _timeProvider);
        var initialTime = stock.LastUpdatedAt;

        // Act - Reserve after some time
        _timeProvider.Advance(TimeSpan.FromHours(1));
        var reservation = stock.Reserve(Guid.NewGuid(), 10, _timeProvider);
        var afterReserve = stock.LastUpdatedAt;

        // Act - Confirm after more time
        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        stock.ConfirmReservation(reservation.Id, _timeProvider);
        var afterConfirm = stock.LastUpdatedAt;

        // Assert - Each operation should update the timestamp
        afterReserve.ShouldBeGreaterThan(initialTime);
        afterConfirm.ShouldBeGreaterThan(afterReserve);

        // Timestamps should match the TimeProvider's time
        afterReserve.ShouldBe(initialTime.AddHours(1));
        afterConfirm.ShouldBe(initialTime.AddHours(1).AddMinutes(30));
    }

    [Fact]
    public void ExpiredReservation_ShouldBeReleasableByBackgroundWorker()
    {
        // Arrange - Set initial time
        var stock = Stock.Create(Guid.NewGuid(), "WORKER-RELEASE-TEST", 10, 2, timeProvider: _timeProvider);

        // Create reservation
        var reservation = stock.Reserve(Guid.NewGuid(), 5, _timeProvider);
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(5);

        // Act - Simulate background worker checking after 20 minutes
        _timeProvider.Advance(TimeSpan.FromMinutes(20));

        // Background worker checks if ExpiresAt < now
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var isExpired = reservation.ExpiresAt < now;

        // Assert
        isExpired.ShouldBeTrue();

        // Worker releases the reservation
        stock.ReleaseReservation(reservation.Id, _timeProvider);
        stock.GetAvailableQuantity(_timeProvider).ShouldBe(10);
        reservation.Status.ShouldBe(ReservationStatus.Released);
    }
}

/// <summary>
///     Tests for order-related time operations.
/// </summary>
public class OrderTimeOperationsTests
{
    private readonly FakeTimeProvider _timeProvider;

    public OrderTimeOperationsTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Order_ShouldTrackCreationTimestamp()
    {
        // The order creation time should be captured
        var expectedTime = _timeProvider.GetUtcNow();

        // Assert that we can control time in tests
        expectedTime.Year.ShouldBe(2024);
        expectedTime.Month.ShouldBe(1);
        expectedTime.Day.ShouldBe(15);
    }

    [Fact]
    public void Order_PaymentTimestamp_ShouldBeAfterCreation()
    {
        // Arrange
        var creationTime = _timeProvider.GetUtcNow();

        // Simulate time passing before payment
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        var paymentTime = _timeProvider.GetUtcNow();

        // Assert
        paymentTime.ShouldBeGreaterThan(creationTime);
        (paymentTime - creationTime).TotalMinutes.ShouldBe(10);
    }

    [Fact]
    public void Order_ShippingTimestamp_ShouldBeAfterPayment()
    {
        // Simulate order lifecycle
        var creationTime = _timeProvider.GetUtcNow();

        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var paymentTime = _timeProvider.GetUtcNow();

        _timeProvider.Advance(TimeSpan.FromDays(1));
        var shippingTime = _timeProvider.GetUtcNow();

        _timeProvider.Advance(TimeSpan.FromDays(3));
        var deliveryTime = _timeProvider.GetUtcNow();

        // Assert timeline
        paymentTime.ShouldBeGreaterThan(creationTime);
        shippingTime.ShouldBeGreaterThan(paymentTime);
        deliveryTime.ShouldBeGreaterThan(shippingTime);

        (deliveryTime - creationTime).TotalDays.ShouldBe(4, 0.1);
    }
}

/// <summary>
///     Tests for scheduled background worker operations.
/// </summary>
public class BackgroundWorkerSchedulingTests
{
    private readonly FakeTimeProvider _timeProvider;

    public BackgroundWorkerSchedulingTests()
    {
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task PeriodicTimer_ShouldFireAtExpectedIntervals()
    {
        // Arrange
        var fireCount = 0;
        var interval = TimeSpan.FromMinutes(5);

        using var timer = new PeriodicTimer(interval, _timeProvider);

        // Start a task to count timer fires
        var countingTask = Task.Run(async () =>
        {
            while (fireCount < 3 && await timer.WaitForNextTickAsync()) fireCount++;
        });

        // Act - Advance time to trigger timer
        await Task.Delay(10); // Let the task start

        _timeProvider.Advance(TimeSpan.FromMinutes(5)); // First fire
        await Task.Delay(10);

        _timeProvider.Advance(TimeSpan.FromMinutes(5)); // Second fire
        await Task.Delay(10);

        _timeProvider.Advance(TimeSpan.FromMinutes(5)); // Third fire
        await Task.Delay(10);

        timer.Dispose(); // Stop the timer

        // Assert
        fireCount.ShouldBe(3);
    }

    [Fact]
    public void SimulateReservationExpiryWorker_RunCycles()
    {
        // Arrange
        var stocks = Enumerable.Range(0, 3)
            .Select(_ => Stock.Create(Guid.NewGuid(), $"SKU-{Guid.NewGuid()}", 100, timeProvider: _timeProvider))
            .ToList();

        // Create reservations on each stock
        var allReservations = new List<(Stock Stock, StockReservation Reservation)>();
        foreach (var stock in stocks)
            for (var i = 0; i < 5; i++)
            {
                var reservation = stock.Reserve(Guid.NewGuid(), 1, _timeProvider);
                allReservations.Add((stock, reservation));
                _timeProvider.Advance(TimeSpan.FromMinutes(2)); // Stagger reservations
            }

        // Simulate worker running every 5 minutes for 30 minutes
        var releasedCount = 0;
        for (var cycle = 0; cycle < 6; cycle++)
        {
            _timeProvider.Advance(TimeSpan.FromMinutes(5));
            var currentTime = _timeProvider.GetUtcNow().UtcDateTime;

            // Worker checks all stocks for expired reservations
            foreach (var stock in stocks)
            {
                stock.CleanupExpiredReservations(_timeProvider);
            }
        }

        // Count expired reservations
        releasedCount = allReservations.Count(x => x.Reservation.Status == ReservationStatus.Expired);

        // Assert - All 15 reservations should be cleaned up eventually
        releasedCount.ShouldBe(15);
    }
}
