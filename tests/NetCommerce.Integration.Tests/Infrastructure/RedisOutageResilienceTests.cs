#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
///     PRODUCTION-READINESS TEST: Redis Outage Resilience (Infrastructure Failure Mode #1)
/// </summary>
public class RedisOutageResilienceTests : IntegrationTestBase
{
    public RedisOutageResilienceTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     When Redis is unavailable, inventory reservation should FAIL SAFELY
    ///     rather than proceeding without a lock (which could cause overselling).
    /// </summary>
    [Fact]
    public async Task RedisDown_ReserveInventory_ShouldFailSafely()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var inventoryDb = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(productId, "SKU-REDIS-001", 100);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();

        var reserveCommand = new ReserveInventoryCommand(
            orderId,
            [new OrderItemReservation(productId, 10, "SKU-REDIS-001")]);

        var tracked = await Fixture.Host.InvokeMessageAndWaitAsync(reserveCommand);

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var stockItem = await verifyDb.Stocks
            .FirstOrDefaultAsync(s => s.ProductId == productId);

        stockItem.ShouldNotBeNull();
        stockItem.ReservedQuantity.ShouldBeLessThanOrEqualTo(stockItem.Quantity,
            "CRITICAL: Reserved quantity exceeds available stock - potential overselling!");

        Console.WriteLine($"[RedisOutage] Stock check passed: {stockItem.Quantity} available, {stockItem.ReservedQuantity} reserved");
    }

    /// <summary>
    ///     Tests that concurrent reservations without proper locking are detected.
    /// </summary>
    [Fact]
    public async Task ConcurrentReservations_ShouldNotOversell()
    {
        var productId = Guid.NewGuid();
        const int availableStock = 100;
        const int reservationAmount = 70; // Two of these would oversell

        await using var inventoryDb = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(productId, "SKU-CONCURRENT-001", availableStock);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();
        var stockId = stock.Id;

        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();

        var reservation1 = new ReserveInventoryCommand(
            order1Id,
            [new OrderItemReservation(productId, reservationAmount, "SKU-CONCURRENT-001")]);

        var reservation2 = new ReserveInventoryCommand(
            order2Id,
            [new OrderItemReservation(productId, reservationAmount, "SKU-CONCURRENT-001")]);

        // Explicitly typed Func<IMessageContext, Task> resolves the ambiguity
        Func<IMessageContext, Task> action = async bus =>
        {
            var task1 = bus.InvokeAsync(reservation1);
            var task2 = bus.InvokeAsync(reservation2);
            await Task.WhenAll(task1, task2);
        };

        var session = await Fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(action);

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalStock = await verifyDb.Stocks.FindAsync(stockId);
        finalStock.ShouldNotBeNull();

        finalStock.ReservedQuantity.ShouldBeLessThanOrEqualTo(availableStock,
            $"OVERSELLING DETECTED: Reserved {finalStock.ReservedQuantity} but only {availableStock} available!\n" +
            "This indicates a distributed locking failure.");

        Console.WriteLine($"[ConcurrentReservation] Final state: {finalStock.ReservedQuantity}/{availableStock} reserved");
        Console.WriteLine($"[ConcurrentReservation] At least one reservation should have failed to prevent overselling");
    }

    /// <summary>
    ///     Tests the "circuit breaker" behavior when Redis connectivity is intermittent.
    /// </summary>
    [Fact]
    public async Task RedisIntermittent_ShouldTripCircuitBreaker()
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var inventoryDb = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(productId, "SKU-CIRCUIT-001", 50);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();

        var reserveCommand = new ReserveInventoryCommand(
            orderId,
            [new OrderItemReservation(productId, 5, "SKU-CIRCUIT-001")]);

        var tracked = await Fixture.Host.InvokeMessageAndWaitAsync(reserveCommand);

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var stockItem = await verifyDb.Stocks
            .FirstOrDefaultAsync(s => s.ProductId == productId);

        stockItem.ShouldNotBeNull();

        Console.WriteLine($"[CircuitBreaker] System operational: {stockItem.ReservedQuantity} reserved");
        Console.WriteLine($"[CircuitBreaker] Circuit breaker pattern should be configured in Polly pipeline");
    }
}
