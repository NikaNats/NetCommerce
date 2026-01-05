// FILE: tests/NetCommerce.Domain.Tests/Workers/TimeOperationsTests.cs

using Microsoft.Extensions.Time.Testing;
using NetCommerce.Inventory.Domain.Stock;
using Shouldly;

namespace NetCommerce.Domain.Tests.Workers;

/// <summary>
///     Pure unit tests for time-sensitive logic and scheduling primitives.
///     Restored from the original suite to ensure deterministic time behavior.
/// </summary>
public class TimeOperationsTests
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task PeriodicTimer_ShouldFireAtExpectedIntervals()
    {
        // Verifies the background worker scheduling primitive
        // Arrange
        var fireCount = 0;
        var interval = TimeSpan.FromMinutes(5);

        using var timer = new PeriodicTimer(interval, _timeProvider);

        // Act - Start a background task
        var task = Task.Run(async () =>
        {
            while (fireCount < 3 && await timer.WaitForNextTickAsync())
                fireCount++;
        });

        // Advance time to trigger timer
        await Task.Delay(10); // Let task start
        _timeProvider.Advance(TimeSpan.FromMinutes(5)); // Tick 1
        await Task.Delay(10);
        _timeProvider.Advance(TimeSpan.FromMinutes(5)); // Tick 2
        await Task.Delay(10);
        _timeProvider.Advance(TimeSpan.FromMinutes(5)); // Tick 3
        await Task.Delay(10);

        // Assert
        fireCount.ShouldBe(3);
    }

    [Fact]
    public void Order_LifecycleTimestamps_ShouldBeChronological()
    {
        // Verifies logical ordering of timestamps in a workflow
        // Arrange
        var creationTime = _timeProvider.GetUtcNow();

        // Simulate: Created
        var orderCreated = creationTime;

        // Simulate: Paid 10 mins later
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        var orderPaid = _timeProvider.GetUtcNow();

        // Simulate: Shipped 1 day later
        _timeProvider.Advance(TimeSpan.FromDays(1));
        var orderShipped = _timeProvider.GetUtcNow();

        // Assert
        orderPaid.ShouldBeGreaterThan(orderCreated);
        orderShipped.ShouldBeGreaterThan(orderPaid);
        (orderShipped - orderCreated).TotalDays.ShouldBe(1, 0.1);
    }

    [Fact]
    public void Stock_LastUpdatedAt_ShouldFollowTimeProvider()
    {
        // Verifies domain entities use the injected time provider
        var stock = Stock.Create(Guid.NewGuid(), "TIME-TEST", 100, timeProvider: _timeProvider);
        var initial = stock.LastUpdatedAt;

        _timeProvider.Advance(TimeSpan.FromHours(1));
        stock.Reserve(Guid.NewGuid(), 1, _timeProvider);

        stock.LastUpdatedAt.ShouldBeGreaterThan(initial);
        stock.LastUpdatedAt.ShouldBe(initial.AddHours(1));
    }
}
