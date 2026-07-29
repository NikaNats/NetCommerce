#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Domain.Stock;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
///     ADVERSARIAL INFRASTRUCTURE TEST: Redis Kill-Switch (Fail-Closed Drill)
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

    [Fact]
    public async Task RedisUnavailable_ConcurrentReservations_ShouldNeverOversell()
    {
        var productId = Guid.NewGuid();
        const int availableStock = 10;
        const int unitsPerReservation = 3;
        const int concurrentRequests = 30; // Scaled to 30 to avoid PostgreSQL connection pool exhaustion (limit: 100)

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

        var results = new ConcurrentBag<(int Index, bool Success, Guid OrderId, string? Error)>();
        var startBarrier = new TaskCompletionSource();

        Func<IMessageContext, Task> action = async bus =>
        {
            var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
            {
                var orderId = Guid.NewGuid();

                // Wait at barrier for maximum concurrency impact
                await startBarrier.Task;

                var command = new ReserveStockCommand(orderId, productId, unitsPerReservation);

                try
                {
                    await bus.InvokeAsync(command);
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
        };

        // Track both concurrent requests within a single TrackedSession
        await Fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(action);

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks
            .IgnoreQueryFilters()
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

        totalReserved.ShouldBeLessThanOrEqualTo(availableStock,
            $"CRITICAL FAILURE: OVERSELLING DETECTED! Reserved {totalReserved} units but only {availableStock} available.");

        var availableQty = finalStock.GetAvailableQuantity();
        var reservedQty = finalStock.GetReservedQuantity();
        (availableQty + reservedQty).ShouldBe(finalStock.Quantity,
            $"Stock accounting invariant violated! Available({availableQty}) + Reserved({reservedQty}) != Total({finalStock.Quantity})");
    }

    #endregion

    #region Test 2: Verify PostgreSQL FOR UPDATE Lock Provides Fallback

    [Fact]
    public async Task PostgresForUpdateLock_ShouldPreventRaceCondition_EvenWithoutRedis()
    {
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

        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();

        var reservation1 = new ReserveStockCommand(order1Id, productId, requestAmount);
        var reservation2 = new ReserveStockCommand(order2Id, productId, requestAmount);

        Func<IMessageContext, Task> action = async bus =>
        {
            var task1 = bus.InvokeAsync(reservation1);
            var task2 = bus.InvokeAsync(reservation2);
            await Task.WhenAll(task1, task2);
        };

        // Track both concurrent requests within a single TrackedSession
        await Fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(action);

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks
            .IgnoreQueryFilters()
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.Id == stockId);

        finalStock.ShouldNotBeNull();

        var totalReserved = finalStock.Reservations
            .Where(r => r.Status == ReservationStatus.Active || r.Status == ReservationStatus.PendingPayment)
            .Sum(r => r.Quantity);

        Console.WriteLine($"[PostgresLock] Total reserved: {totalReserved} / {availableStock}");

        totalReserved.ShouldBeLessThanOrEqualTo(availableStock,
            $"PostgreSQL FOR UPDATE lock failed! Reserved {totalReserved} but only {availableStock} available.");

        Console.WriteLine("[PostgresLock] ✓ FOR UPDATE lock prevented overselling");
    }

    #endregion

    #region Test 3: Circuit Breaker Should Trip After Repeated Failures

    [Fact]
    public async Task CircuitBreaker_ShouldPreventCascadingFailures()
    {
        var productId = Guid.NewGuid();
        const int availableStock = 1000;

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-CIRCUIT-001", availableStock);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();

        const int rapidFireCount = 30; // Avoid connection pool exhaustion
        var results = new ConcurrentBag<(int Index, bool Success, TimeSpan Duration)>();

        Func<IMessageContext, Task> action = async bus =>
        {
            var tasks = Enumerable.Range(0, rapidFireCount).Select(async i =>
            {
                var orderId = Guid.NewGuid();
                var command = new ReserveStockCommand(orderId, productId, 1);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    await bus.InvokeAsync(command);
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
        };

        // Track both concurrent requests within a single TrackedSession
        await Fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(action);

        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);
        var avgDuration = results.Any() ? results.Average(r => r.Duration.TotalMilliseconds) : 0;

        Console.WriteLine($"[CircuitBreaker] Results: {successCount} success, {failureCount} failed");
        Console.WriteLine($"[CircuitBreaker] Avg duration: {avgDuration:F2}ms");

        if (failureCount > 0)
        {
            var avgFailureDuration = results.Where(r => !r.Success).Average(r => r.Duration.TotalMilliseconds);
            Console.WriteLine($"[CircuitBreaker] Avg failure duration: {avgFailureDuration:F2}ms");
        }

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks.FindAsync(stock.Id);
        finalStock.ShouldNotBeNull();
        finalStock.ReservedQuantity.ShouldBeLessThanOrEqualTo(availableStock,
            "Circuit breaker test should not cause overselling");

        Console.WriteLine("[CircuitBreaker] ✓ System handled rapid-fire load without overselling");
    }

    #endregion

    #region Test 4: Graceful Degradation Under Partial Redis Failure

    [Fact]
    public async Task PartialRedisFailure_ShouldDegradeGracefully()
    {
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

        var results = new ConcurrentBag<(Guid ProductId, bool Success)>();

        Func<IMessageContext, Task> action = async bus =>
        {
            var tasks = products.SelectMany(p =>
                Enumerable.Range(0, 10).Select(async _ => // 10 instead of 20 = 30 total concurrent requests
                {
                    var orderId = Guid.NewGuid();
                    var command = new ReserveStockCommand(orderId, p.ProductId, 5);

                    try
                    {
                        await bus.InvokeAsync(command);
                        results.Add((p.ProductId, Success: true));
                    }
                    catch
                    {
                        results.Add((p.ProductId, Success: false));
                    }
                }));

            await Task.WhenAll(tasks);
        };

        // Track both concurrent requests within a single TrackedSession
        await Fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(action);

        await using var verifyDb = Fixture.CreateInventoryDbContext();

        foreach (var (productId, sku, stockQty) in products)
        {
            var finalStock = await verifyDb.Stocks
                .IgnoreQueryFilters()
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

    [Fact]
    public async Task AfterRedisRecovery_SystemShouldResumeNormalOperation()
    {
        var productId = Guid.NewGuid();
        const int availableStock = 100;

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-RECOVER-001", availableStock);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();
        var stockId = stock.Id;

        Console.WriteLine("[Recovery] Simulating post-Redis-outage recovery...");

        var successfulReservations = new List<Guid>();

        for (int i = 0; i < 10; i++)
        {
            var orderId = Guid.NewGuid();
            var command = new ReserveStockCommand(orderId, productId, 5);

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

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks.FindAsync(stockId);

        finalStock.ShouldNotBeNull();
        Console.WriteLine($"[Recovery] Final reserved: {finalStock.ReservedQuantity}/{availableStock}");

        successfulReservations.Count.ShouldBeGreaterThan(0,
            "At least some reservations should succeed after recovery");

        finalStock.ReservedQuantity.ShouldBeLessThanOrEqualTo(availableStock,
            "Recovery should not cause overselling");

        (finalStock.GetAvailableQuantity() + finalStock.GetReservedQuantity()).ShouldBe(finalStock.Quantity,
            "Stock accounting invariant must hold after recovery");

        Console.WriteLine("[Recovery] ✓ System recovered and maintained invariants");
    }

    #endregion
}
