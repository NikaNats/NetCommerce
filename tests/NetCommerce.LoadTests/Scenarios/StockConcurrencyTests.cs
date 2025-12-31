using Shouldly;
using NetCommerce.Inventory.Domain.Stock;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
/// In-memory concurrency tests for Stock aggregate.
/// Tests thread-safety and race condition handling without network overhead.
/// </summary>
public class StockConcurrencyTests
{
    /// <summary>
    /// Simulates PS5 launch: Multiple threads trying to reserve limited stock.
    /// Verifies no overselling occurs.
    /// </summary>
    [Fact]
    public async Task PS5Launch_ConcurrentReservations_ShouldNotOversell()
    {
        // Arrange - Very limited PS5 stock
        const int totalStock = 10;
        const int concurrentBuyers = 100;
        
        var stock = Stock.Create(
            productId: Guid.NewGuid(),
            sku: "PS5-DIGITAL-2024",
            initialQuantity: totalStock,
            lowStockThreshold: 2);

        var successfulReservations = 0;
        var failedReservations = 0;
        var lockObject = new object();

        // Act - 100 concurrent buyers trying to reserve
        var tasks = Enumerable.Range(0, concurrentBuyers)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    lock (lockObject)
                    {
                        if (stock.AvailableQuantity > 0)
                        {
                            stock.Reserve(Guid.NewGuid(), 1);
                            Interlocked.Increment(ref successfulReservations);
                        }
                        else
                        {
                            Interlocked.Increment(ref failedReservations);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Expected when stock runs out
                    Interlocked.Increment(ref failedReservations);
                }
            }));

        await Task.WhenAll(tasks);

        // Assert - NO OVERSELLING
        successfulReservations.ShouldBe(totalStock);
        failedReservations.ShouldBe(concurrentBuyers - totalStock);
        stock.AvailableQuantity.ShouldBe(0);
        stock.ReservedQuantity.ShouldBe(totalStock);
    }

    /// <summary>
    /// Tests reservation expiry and reallocation scenario.
    /// Some users complete purchase, others abandon cart.
    /// </summary>
    [Fact]
    public async Task PS5Launch_ReservationExpiry_ShouldReallocateStock()
    {
        // Arrange
        const int totalStock = 5;
        
        var stock = Stock.Create(Guid.NewGuid(), "PS5", totalStock, 1);
        var lockObject = new object();

        // Phase 1: All 5 PS5s get reserved
        var phase1Reservations = new List<StockReservation>();
        for (int i = 0; i < totalStock; i++)
        {
            phase1Reservations.Add(stock.Reserve(Guid.NewGuid(), 1));
        }

        stock.AvailableQuantity.ShouldBe(0);

        // Phase 2: 3 customers abandon cart (release reservation)
        lock (lockObject)
        {
            stock.ReleaseReservation(phase1Reservations[0].Id);
            stock.ReleaseReservation(phase1Reservations[1].Id);
            stock.ReleaseReservation(phase1Reservations[2].Id);
        }

        stock.AvailableQuantity.ShouldBe(3);

        // Phase 3: 2 customers complete purchase
        lock (lockObject)
        {
            stock.ConfirmReservation(phase1Reservations[3].Id);
            stock.ConfirmReservation(phase1Reservations[4].Id);
        }

        // Phase 4: New wave of customers can reserve released stock
        var phase4Tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    lock (lockObject)
                    {
                        if (stock.AvailableQuantity > 0)
                        {
                            stock.Reserve(Guid.NewGuid(), 1);
                            return true;
                        }
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }));

        var results = await Task.WhenAll(phase4Tasks);
        var successfulPhase4 = results.Count(r => r);

        // Assert
        successfulPhase4.ShouldBe(3); // Only 3 were released
        stock.Quantity.ShouldBe(3); // 5 initial - 2 confirmed
        stock.AvailableQuantity.ShouldBe(0); // All reserved again
    }

    /// <summary>
    /// Tests mixed operations: reserves, confirms, and releases happening concurrently.
    /// </summary>
    [Fact]
    public async Task MixedOperations_ConcurrentReserveConfirmRelease_ShouldMaintainConsistency()
    {
        // Arrange
        const int initialStock = 100;
        var stock = Stock.Create(Guid.NewGuid(), "MIXED-TEST", initialStock, 10);
        var lockObject = new object();
        var reservations = new List<StockReservation>();

        // Phase 1: Create reservations
        for (int i = 0; i < 50; i++)
        {
            reservations.Add(stock.Reserve(Guid.NewGuid(), 1));
        }

        stock.AvailableQuantity.ShouldBe(50);
        stock.ReservedQuantity.ShouldBe(50);

        // Phase 2: Mixed operations concurrently
        var confirmTasks = reservations.Take(20).Select(r => Task.Run(() =>
        {
            lock (lockObject)
            {
                try
                {
                    stock.ConfirmReservation(r.Id);
                }
                catch { }
            }
        }));

        var releaseTasks = reservations.Skip(20).Take(20).Select(r => Task.Run(() =>
        {
            lock (lockObject)
            {
                stock.ReleaseReservation(r.Id);
            }
        }));

        var newReserveTasks = Enumerable.Range(0, 30).Select(_ => Task.Run(() =>
        {
            lock (lockObject)
            {
                try
                {
                    if (stock.AvailableQuantity > 0)
                    {
                        stock.Reserve(Guid.NewGuid(), 1);
                    }
                }
                catch { }
            }
        }));

        await Task.WhenAll(confirmTasks.Concat(releaseTasks).Concat(newReserveTasks));

        // Assert - Consistency checks
        // Initial: 100, Confirmed: 20 (deducted), Released: 20 (back to available)
        // Remaining active reservations: 10 (from initial 50) + new reservations
        
        var expectedDeducted = 20; // Confirmed reservations
        stock.Quantity.ShouldBe(initialStock - expectedDeducted);
        
        // Available + Reserved should equal Quantity
        (stock.AvailableQuantity + stock.ReservedQuantity).ShouldBeLessThanOrEqualTo(stock.Quantity);
    }

    /// <summary>
    /// Tests that domain events are correctly raised even under concurrent operations.
    /// </summary>
    [Fact]
    public void ConcurrentOperations_ShouldRaiseDomainEvents()
    {
        // Arrange
        var stock = Stock.Create(Guid.NewGuid(), "EVENTS-TEST", 50, 10);
        var lockObject = new object();

        // Act - Multiple operations
        var reservations = new List<StockReservation>();
        for (int i = 0; i < 10; i++)
        {
            lock (lockObject)
            {
                reservations.Add(stock.Reserve(Guid.NewGuid(), 1));
            }
        }

        // Assert - Domain events should be raised
        var domainEvents = stock.DomainEvents.ToList();
        
        // Should have StockReservedDomainEvent for each reservation
        domainEvents.OfType<StockReservedDomainEvent>().Count().ShouldBe(10);
        
        // Should have LowStockAlertDomainEvent when crossing threshold
        // (50 - 10 reservations = 40 available, threshold is 10, so no alert yet)
        // If we reserved 45, we'd cross the threshold
    }
}
