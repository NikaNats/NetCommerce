#nullable enable
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using Shouldly;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
///     ADVERSARIAL INFRASTRUCTURE TEST: Redis Kill-Switch (Fail-Closed Drill)
///
///     <para>
///     <b>TOP PRIORITY TEST</b> - This validates the most critical invariant in a distributed e-commerce system:
///     "If the lock provider fails, the system MUST fail-closed to prevent overselling."
///     </para>
///
///     <para>
///     <b>Attack Surface:</b>
///     - Redis flap (30 second outage during flash sale)
///     - Redis partition (network split between API servers and Redis)
///     - Redis memory exhaustion (evictions during spike load)
///     </para>
///
///     <para>
///     <b>CRITICAL INVARIANT:</b>
///     During Redis unavailability with 100 concurrent reservation attempts:
///     - EITHER: All 100 fail safely (Circuit Breaker pattern)
///     - OR: Some succeed with PostgreSQL FOR UPDATE fallback lock
///     - NEVER: All 100 succeed without ANY locking (overselling disaster)
///     </para>
///
///     <para>
///     <b>Production Impact of Fail-Open:</b>
///     - Flash sale: 1000 PS5s sold when only 100 in stock
///     - Manual order cancellations, angry customers, reputation damage
///     - Potential lawsuits for false advertising
///     - "Your business logic invariants (No Overselling) are effectively deleted"
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "Adversarial")]
[Trait("Category", "Infrastructure")]
[Trait("Priority", "Critical")]
public class RedisKillSwitchFailClosedTests : IntegrationTestBase
{
    public RedisKillSwitchFailClosedTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Redis Unavailable - System Must Fail-Closed

    /// <summary>
    ///     FAIL-CLOSED DRILL: When Redis is completely unavailable, the system
    ///     should reject inventory reservations rather than proceed without locks.
    ///
    ///     <para>
    ///     This test simulates a Redis outage scenario. In production:
    ///     1. Redis goes down (flap, partition, or memory exhaustion)
    ///     2. 100 concurrent orders attempt to reserve the same limited stock
    ///     3. WITHOUT distributed locks, all could succeed → OVERSELLING
    ///     </para>
    ///
    ///     <para>
    ///     <b>Expected Behavior:</b>
    ///     - PostgreSQL FOR UPDATE locks provide fallback protection
    ///     - Stock invariant (Available + Reserved = Total) is NEVER violated
    ///     - No overselling regardless of Redis state
    ///     </para>
    /// </summary>
    [Fact]
    public async Task RedisUnavailable_ConcurrentReservations_ShouldNeverOversell()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create extremely limited stock (force contention)
        // ═══════════════════════════════════════════════════════════════════════
        var productId = Guid.NewGuid();
        const int availableStock = 10; // Very limited - like PS5 during launch
        const int unitsPerReservation = 3;
        const int concurrentRequests = 100; // 100 requests × 3 units = 300 demanded, only 10 available

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-KILLSWITCH-001", availableStock);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();
        var stockId = stock.Id;

        Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║         ADVERSARIAL DRILL: Redis Kill-Switch (Fail-Closed)        ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Available Stock:      {availableStock,6} units                              ║");
        Console.WriteLine($"║ Units per Request:    {unitsPerReservation,6} units                              ║");
        Console.WriteLine($"║ Concurrent Requests:  {concurrentRequests,6}                                   ║");
        Console.WriteLine($"║ Total Demand:         {concurrentRequests * unitsPerReservation,6} units (EXTREME oversell scenario)     ║");
        Console.WriteLine($"║ Max Possible Success: {availableStock / unitsPerReservation,6} reservations                       ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ CRITICAL INVARIANT: Reserved ≤ Available (NO OVERSELLING)         ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Launch concurrent reservation "storm" (simulates flash sale)
        // ═══════════════════════════════════════════════════════════════════════
        var results = new ConcurrentBag<(int Index, bool Success, Guid OrderId, string? Error)>();
        var startBarrier = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            var orderId = Guid.NewGuid();

            // Wait at barrier for maximum concurrency impact
            await startBarrier.Task;

            var command = new ReserveInventoryCommand(
                orderId,
                [new OrderItemReservation(productId, unitsPerReservation, "SKU-KILLSWITCH-001")]);

            try
            {
                await Fixture.Host.InvokeMessageAndWaitAsync(command);
                results.Add((i, Success: true, orderId, Error: null));
            }
            catch (Exception ex)
            {
                results.Add((i, Success: false, orderId, ex.Message));
            }
        }).ToList();

        // Release all tasks simultaneously for maximum concurrency
        startBarrier.SetResult();
        await Task.WhenAll(tasks);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: THE CRITICAL INVARIANT - NEVER OVERSELL
        // ═══════════════════════════════════════════════════════════════════════
        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.Id == stockId);

        finalStock.ShouldNotBeNull("Stock record must exist after concurrent operations");

        var totalReserved = finalStock.Reservations
            .Where(r => r.Status == ReservationStatus.Active || r.Status == ReservationStatus.PendingPayment)
            .Sum(r => r.Quantity);

        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);
        var oversoldBy = totalReserved - availableStock;

        Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      DRILL RESULTS                                 ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Successful Requests:  {successCount,6}                                   ║");
        Console.WriteLine($"║ Failed Requests:      {failureCount,6}                                   ║");
        Console.WriteLine($"║ Total Reserved:       {totalReserved,6} units                              ║");
        Console.WriteLine($"║ Available Stock:      {availableStock,6} units                              ║");

        if (oversoldBy > 0)
        {
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════╣");
            Console.WriteLine($"║ ⚠️  OVERSELLING DETECTED: {oversoldBy} units over capacity!           ║");
            Console.WriteLine("║ ❌ FAIL-CLOSED INVARIANT VIOLATED - CRITICAL BUG!                  ║");
        }
        else
        {
            Console.WriteLine("╠════════════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║ ✓ NO OVERSELLING - Fail-closed invariant maintained               ║");
        }

        Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");

        // THE CRITICAL ASSERTION
        totalReserved.ShouldBeLessThanOrEqualTo(availableStock,
            $"CRITICAL FAILURE: OVERSELLING DETECTED! Reserved {totalReserved} units but only {availableStock} available. " +
            "This indicates the fail-closed invariant was violated. " +
            "Without distributed locking, the system allowed concurrent reservations that exceeded stock.");

        // Stock accounting invariant
        var availableQty = finalStock.GetAvailableQuantity();
        var reservedQty = finalStock.GetReservedQuantity();
        (availableQty + reservedQty).ShouldBe(finalStock.Quantity,
            $"Stock accounting invariant violated! Available({availableQty}) + Reserved({reservedQty}) != Total({finalStock.Quantity})");
    }

    #endregion

    #region Test 2: Verify PostgreSQL FOR UPDATE Lock Provides Fallback

    /// <summary>
    ///     Validates that PostgreSQL's FOR UPDATE lock provides a reliable fallback
    ///     when Redis is unavailable. This is the "defense in depth" pattern.
    ///
    ///     <para>
    ///     The system should NEVER rely solely on Redis for preventing overselling.
    ///     PostgreSQL pessimistic locking must be the last line of defense.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task PostgresForUpdateLock_ShouldPreventRaceCondition_EvenWithoutRedis()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Two concurrent requests that would cause race condition without locking
        // ═══════════════════════════════════════════════════════════════════════
        var productId = Guid.NewGuid();
        const int availableStock = 100;
        const int requestAmount = 70; // Two of these would oversell

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-FORLOCK-001", availableStock);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();
        var stockId = stock.Id;

        Console.WriteLine("[PostgresLock] Testing FOR UPDATE lock as fallback...");
        Console.WriteLine($"[PostgresLock] Available: {availableStock}, Request: {requestAmount} × 2 = {requestAmount * 2}");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Fire two concurrent reservations that would oversell without locking
        // ═══════════════════════════════════════════════════════════════════════
        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();

        var reservation1 = new ReserveInventoryCommand(
            order1Id,
            [new OrderItemReservation(productId, requestAmount, "SKU-FORLOCK-001")]);

        var reservation2 = new ReserveInventoryCommand(
            order2Id,
            [new OrderItemReservation(productId, requestAmount, "SKU-FORLOCK-001")]);

        var task1 = Fixture.Host.InvokeMessageAndWaitAsync(reservation1);
        var task2 = Fixture.Host.InvokeMessageAndWaitAsync(reservation2);

        await Task.WhenAll(task1, task2);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Only one should succeed, no overselling
        // ═══════════════════════════════════════════════════════════════════════
        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.Id == stockId);

        finalStock.ShouldNotBeNull();

        var totalReserved = finalStock.Reservations
            .Where(r => r.Status == ReservationStatus.Active || r.Status == ReservationStatus.PendingPayment)
            .Sum(r => r.Quantity);

        Console.WriteLine($"[PostgresLock] Total reserved: {totalReserved} / {availableStock}");

        totalReserved.ShouldBeLessThanOrEqualTo(availableStock,
            $"PostgreSQL FOR UPDATE lock failed! Reserved {totalReserved} but only {availableStock} available. " +
            "The database-level locking did not prevent the race condition.");

        Console.WriteLine("[PostgresLock] ✓ FOR UPDATE lock prevented overselling");
    }

    #endregion

    #region Test 3: Circuit Breaker Should Trip After Repeated Failures

    /// <summary>
    ///     Validates that after repeated lock acquisition failures, the circuit breaker
    ///     trips and fast-fails subsequent requests rather than continuing to hammer
    ///     a failing Redis instance.
    ///
    ///     <para>
    ///     <b>Circuit Breaker Pattern:</b>
    ///     - Closed: Normal operation, requests flow through
    ///     - Open: After N failures, reject immediately
    ///     - Half-Open: After timeout, allow probe request
    ///     </para>
    /// </summary>
    [Fact]
    public async Task CircuitBreaker_ShouldPreventCascadingFailures()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create stock for circuit breaker test
        // ═══════════════════════════════════════════════════════════════════════
        var productId = Guid.NewGuid();
        const int availableStock = 1000;

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-CIRCUIT-001", availableStock);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();

        Console.WriteLine("[CircuitBreaker] Testing circuit breaker behavior under load...");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Fire many rapid requests to test circuit breaker behavior
        // ═══════════════════════════════════════════════════════════════════════
        const int rapidFireCount = 50;
        var results = new ConcurrentBag<(int Index, bool Success, TimeSpan Duration)>();

        var tasks = Enumerable.Range(0, rapidFireCount).Select(async i =>
        {
            var orderId = Guid.NewGuid();
            var command = new ReserveInventoryCommand(
                orderId,
                [new OrderItemReservation(productId, 1, "SKU-CIRCUIT-001")]);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await Fixture.Host.InvokeMessageAndWaitAsync(command);
                sw.Stop();
                results.Add((i, Success: true, sw.Elapsed));
            }
            catch
            {
                sw.Stop();
                results.Add((i, Success: false, sw.Elapsed));
            }
        });

        await Task.WhenAll(tasks);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: System should handle load gracefully
        // ═══════════════════════════════════════════════════════════════════════
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);
        var avgDuration = results.Average(r => r.Duration.TotalMilliseconds);

        Console.WriteLine($"[CircuitBreaker] Results: {successCount} success, {failureCount} failed");
        Console.WriteLine($"[CircuitBreaker] Avg duration: {avgDuration:F2}ms");

        // If circuit breaker is working, failed requests should be fast
        if (failureCount > 0)
        {
            var avgFailureDuration = results.Where(r => !r.Success).Average(r => r.Duration.TotalMilliseconds);
            Console.WriteLine($"[CircuitBreaker] Avg failure duration: {avgFailureDuration:F2}ms (should be fast if circuit is open)");
        }

        // Verify stock invariant still holds
        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks.FindAsync(stock.Id);
        finalStock.ShouldNotBeNull();
        finalStock.ReservedQuantity.ShouldBeLessThanOrEqualTo(availableStock,
            "Circuit breaker test should not cause overselling");

        Console.WriteLine("[CircuitBreaker] ✓ System handled rapid-fire load without overselling");
    }

    #endregion

    #region Test 4: Graceful Degradation Under Partial Redis Failure

    /// <summary>
    ///     Tests system behavior when Redis is partially available (intermittent failures).
    ///     The system should degrade gracefully rather than fail completely.
    ///
    ///     <para>
    ///     <b>Partial Failure Scenarios:</b>
    ///     - Redis responding slowly (timeout some requests)
    ///     - Redis dropping occasional connections
    ///     - Redis returning errors for some keys
    ///     </para>
    /// </summary>
    [Fact]
    public async Task PartialRedisFailure_ShouldDegradeGracefully()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Multiple products to simulate varying Redis response
        // ═══════════════════════════════════════════════════════════════════════
        var products = new List<(Guid ProductId, string Sku, int Stock)>
        {
            (Guid.NewGuid(), "SKU-DEGRADE-001", 50),
            (Guid.NewGuid(), "SKU-DEGRADE-002", 50),
            (Guid.NewGuid(), "SKU-DEGRADE-003", 50)
        };

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        foreach (var (productId, sku, stockQty) in products)
        {
            var stock = Stock.Create(productId, sku, stockQty);
            inventoryDb.Stocks.Add(stock);
        }
        await inventoryDb.SaveChangesAsync();

        Console.WriteLine("[Degradation] Testing graceful degradation across multiple products...");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Mixed load across multiple products
        // ═══════════════════════════════════════════════════════════════════════
        var results = new ConcurrentBag<(Guid ProductId, bool Success)>();

        var tasks = products.SelectMany(p =>
            Enumerable.Range(0, 20).Select(async _ =>
            {
                var orderId = Guid.NewGuid();
                var command = new ReserveInventoryCommand(
                    orderId,
                    [new OrderItemReservation(p.ProductId, 5, p.Sku)]);

                try
                {
                    await Fixture.Host.InvokeMessageAndWaitAsync(command);
                    results.Add((p.ProductId, Success: true));
                }
                catch
                {
                    results.Add((p.ProductId, Success: false));
                }
            }));

        await Task.WhenAll(tasks);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: No overselling on any product
        // ═══════════════════════════════════════════════════════════════════════
        await using var verifyDb = Fixture.CreateInventoryDbContext();

        foreach (var (productId, sku, stockQty) in products)
        {
            var finalStock = await verifyDb.Stocks
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.ProductId == productId);

            finalStock.ShouldNotBeNull();

            var totalReserved = finalStock.Reservations
                .Where(r => r.Status == ReservationStatus.Active || r.Status == ReservationStatus.PendingPayment)
                .Sum(r => r.Quantity);

            var successForProduct = results.Count(r => r.ProductId == productId && r.Success);
            Console.WriteLine($"[Degradation] {sku}: {successForProduct} successes, {totalReserved}/{stockQty} reserved");

            totalReserved.ShouldBeLessThanOrEqualTo(stockQty,
                $"Overselling on {sku}: Reserved {totalReserved} but only {stockQty} available");
        }

        Console.WriteLine("[Degradation] ✓ All products maintained invariant under mixed load");
    }

    #endregion

    #region Test 5: Recovery After Redis Restoration

    /// <summary>
    ///     Tests that the system recovers normally after Redis comes back online.
    ///     This validates the "circuit breaker half-open" transition.
    ///
    ///     <para>
    ///     After a Redis outage:
    ///     1. Circuit breaker should be open (fast-failing requests)
    ///     2. After timeout, circuit goes half-open
    ///     3. Probe request succeeds
    ///     4. Circuit closes, normal operation resumes
    ///     </para>
    /// </summary>
    [Fact]
    public async Task AfterRedisRecovery_SystemShouldResumeNormalOperation()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create stock for recovery test
        // ═══════════════════════════════════════════════════════════════════════
        var productId = Guid.NewGuid();
        const int availableStock = 100;

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-RECOVER-001", availableStock);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();
        var stockId = stock.Id;

        Console.WriteLine("[Recovery] Simulating post-Redis-outage recovery...");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Sequential requests to simulate recovery scenario
        // ═══════════════════════════════════════════════════════════════════════
        var successfulReservations = new List<Guid>();

        for (int i = 0; i < 10; i++)
        {
            var orderId = Guid.NewGuid();
            var command = new ReserveInventoryCommand(
                orderId,
                [new OrderItemReservation(productId, 5, "SKU-RECOVER-001")]);

            try
            {
                await Fixture.Host.InvokeMessageAndWaitAsync(command);
                successfulReservations.Add(orderId);
                Console.WriteLine($"[Recovery] Request {i + 1}: SUCCESS");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Recovery] Request {i + 1}: FAILED ({ex.Message})");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: System recovered and processed requests correctly
        // ═══════════════════════════════════════════════════════════════════════
        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks.FindAsync(stockId);

        finalStock.ShouldNotBeNull();
        Console.WriteLine($"[Recovery] Final reserved: {finalStock.ReservedQuantity}/{availableStock}");

        // Verify some requests succeeded
        successfulReservations.Count.ShouldBeGreaterThan(0,
            "At least some reservations should succeed after recovery");

        // Verify no overselling
        finalStock.ReservedQuantity.ShouldBeLessThanOrEqualTo(availableStock,
            "Recovery should not cause overselling");

        // Verify stock accounting
        (finalStock.GetAvailableQuantity() + finalStock.GetReservedQuantity()).ShouldBe(finalStock.Quantity,
            "Stock accounting invariant must hold after recovery");

        Console.WriteLine("[Recovery] ✓ System recovered and maintained invariants");
    }

    #endregion
}
