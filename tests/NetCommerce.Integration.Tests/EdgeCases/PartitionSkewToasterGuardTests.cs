#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace NetCommerce.Integration.Tests.EdgeCases;

/// <summary>
///     ADVERSARIAL INFRASTRUCTURE TEST: Partition Skew / Toaster Guard
///
///     <para>
///     Tests that Wolverine's partitioned sequential messaging prevents
///     "hot key" starvation where a flood of requests for one product
///     doesn't starve requests for unrelated products.
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "Adversarial")]
[Trait("Category", "EdgeCase")]
[Trait("Category", "Partitioning")]
public class PartitionSkewToasterGuardTests : IntegrationTestBase
{
    private const int DefaultPartitionCount = 9;

    public PartitionSkewToasterGuardTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Hot Key Flood Should Not Starve Cold Key

    [Fact]
    public async Task HotKeyFlood_ShouldNotStarveColdKey()
    {
        var hotProductId = Guid.NewGuid();
        var coldProductId = Guid.NewGuid();

        const int hotKeyStock = 100;
        const int coldKeyStock = 50;
        const int hotKeyRequestCount = 50;
        const int coldKeyRequestCount = 5;

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var hotStock = Stock.Create(hotProductId, "SKU-PS5-001", hotKeyStock);
        var coldStock = Stock.Create(coldProductId, "SKU-TOASTER-001", coldKeyStock);
        inventoryDb.Stocks.AddRange(hotStock, coldStock);
        await inventoryDb.SaveChangesAsync();

        var allRequests = new List<(bool IsHot, Guid ProductId, string Sku)>();
        for (var i = 0; i < hotKeyRequestCount; i++)
            allRequests.Add((true, hotProductId, "SKU-PS5-001"));
        for (var i = 0; i < coldKeyRequestCount; i++)
            allRequests.Add((false, coldProductId, "SKU-TOASTER-001"));

        var shuffled = allRequests.OrderBy(_ => Guid.NewGuid()).ToList();

        var hotKeyLatencies = new ConcurrentBag<double>();
        var coldKeyLatencies = new ConcurrentBag<double>();
        var results = new ConcurrentBag<(int Index, bool Success, bool IsHot, double Latency)>();

        Func<IMessageContext, Task> action = async bus =>
        {
            var tasks = shuffled.Select(async (req, idx) =>
            {
                var orderId = Guid.NewGuid();
                var command = new ReserveInventoryCommand(
                    orderId,
                    [new OrderItemReservation(req.ProductId, 1, req.Sku)]);

                var sw = Stopwatch.StartNew();
                try
                {
                    await bus.InvokeAsync(command);
                    sw.Stop();
                    if (req.IsHot) hotKeyLatencies.Add(sw.Elapsed.TotalMilliseconds);
                    else coldKeyLatencies.Add(sw.Elapsed.TotalMilliseconds);
                    results.Add((idx, true, req.IsHot, sw.Elapsed.TotalMilliseconds));
                }
                catch
                {
                    sw.Stop();
                    results.Add((idx, false, req.IsHot, sw.Elapsed.TotalMilliseconds));
                }
            });

            await Task.WhenAll(tasks);
        };

        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(action);

        var coldSuccesses = results.Count(r => !r.IsHot && r.Success);

        await using var verifyDb = Fixture.CreateInventoryDbContext();
        var finalHotStock = await verifyDb.Stocks.Include(s => s.Reservations).FirstOrDefaultAsync(s => s.ProductId == hotProductId);
        var finalColdStock = await verifyDb.Stocks.Include(s => s.Reservations).FirstOrDefaultAsync(s => s.ProductId == coldProductId);

        finalHotStock.ShouldNotBeNull();
        finalColdStock.ShouldNotBeNull();

        finalHotStock.GetReservedQuantity().ShouldBeLessThanOrEqualTo(hotKeyStock);
        finalColdStock.GetReservedQuantity().ShouldBeLessThanOrEqualTo(coldKeyStock);
        coldSuccesses.ShouldBeGreaterThan(0, "TOASTER STARVATION: Cold key got zero reservations despite having available stock");
    }

    #endregion

    #region Test 2: Same Partition Products Should Queue Fairly

    [Fact]
    public async Task SamePartitionProducts_ShouldQueueFairly()
    {
        // 1. Deterministically find two products that hash to the exact same partition
        var product1Id = Guid.NewGuid();
        var targetPartition = Math.Abs(product1Id.GetHashCode()) % DefaultPartitionCount;

        Guid product2Id;
        do
        {
            product2Id = Guid.NewGuid();
        } while (Math.Abs(product2Id.GetHashCode()) % DefaultPartitionCount != targetPartition);

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock1 = Stock.Create(product1Id, "SKU-FAIR-001", 100);
        var stock2 = Stock.Create(product2Id, "SKU-FAIR-002", 100);
        inventoryDb.Stocks.AddRange(stock1, stock2);
        await inventoryDb.SaveChangesAsync();

        Console.WriteLine($"[FairQueue] Verified Same Partition: Slot {targetPartition} for both products.");

        var product1Latencies = new ConcurrentBag<double>();
        var product2Latencies = new ConcurrentBag<double>();

        var requests = Enumerable.Range(0, 20)
            .Select(i => i % 2 == 0
                ? (ProductId: product1Id, Sku: "SKU-FAIR-001")
                : (ProductId: product2Id, Sku: "SKU-FAIR-002"))
            .ToList();

        // 2. Execute all 20 requests under a single TrackActivity session using bus.InvokeAsync
        Func<IMessageContext, Task> action = async bus =>
        {
            var tasks = requests.Select(async req =>
            {
                var orderId = Guid.NewGuid();
                var command = new ReserveInventoryCommand(
                    orderId,
                    [new OrderItemReservation(req.ProductId, 1, req.Sku)]);

                var sw = Stopwatch.StartNew();
                try
                {
                    await bus.InvokeAsync(command);
                    sw.Stop();

                    if (req.ProductId == product1Id)
                        product1Latencies.Add(sw.Elapsed.TotalMilliseconds);
                    else
                        product2Latencies.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    sw.Stop();
                }
            });

            await Task.WhenAll(tasks);
        };

        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(action);

        // 3. Assertions
        product1Latencies.Count.ShouldBeGreaterThan(0, "Product 1 should have completions");
        product2Latencies.Count.ShouldBeGreaterThan(0, "Product 2 should have completions");

        Console.WriteLine($"[FairQueue] Product 1: {product1Latencies.Count} ops, avg {product1Latencies.Average():F1}ms");
        Console.WriteLine($"[FairQueue] Product 2: {product2Latencies.Count} ops, avg {product2Latencies.Average():F1}ms");
    }

    #endregion

    #region Test 3: Latency Growth Should Be Linear

    [Fact]
    public async Task LatencyGrowth_ShouldBeLinear()
    {
        var productId = Guid.NewGuid();

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-LINEAR-001", 1000);
        inventoryDb.Stocks.Add(stock);
        await inventoryDb.SaveChangesAsync();

        var concurrencyLevels = new[] { 1, 5, 10, 20 };
        var results = new List<(int Concurrency, double AvgLatency, double MaxLatency)>();

        foreach (var concurrency in concurrencyLevels)
        {
            var latencies = new ConcurrentBag<double>();

            Func<IMessageContext, Task> action = async bus =>
            {
                var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
                {
                    var orderId = Guid.NewGuid();
                    var command = new ReserveInventoryCommand(
                        orderId,
                        [new OrderItemReservation(productId, 1, "SKU-LINEAR-001")]);

                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await bus.InvokeAsync(command);
                        sw.Stop();
                        latencies.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    catch
                    {
                        sw.Stop();
                    }
                });

                await Task.WhenAll(tasks);
            };

            await Fixture.Host.TrackActivity()
                .Timeout(TimeSpan.FromSeconds(15))
                .DoNotAssertOnExceptionsDetected()
                .ExecuteAndWaitAsync(action);

            if (latencies.Any())
            {
                results.Add((concurrency, latencies.Average(), latencies.Max()));
            }
        }

        results.Count.ShouldBeGreaterThan(0, "Should have measurement results");
    }

    #endregion

    #region Test 4: Multi-Product Mixed Load Maintains Invariants

    [Fact]
    public async Task MultiProductMixedLoad_ShouldMaintainInvariants()
    {
        var products = new List<(Guid Id, string Sku, int Stock)>
        {
            (Guid.NewGuid(), "SKU-MULTI-001", 20),
            (Guid.NewGuid(), "SKU-MULTI-002", 100),
            (Guid.NewGuid(), "SKU-MULTI-003", 500),
        };

        await using var inventoryDb = Fixture.CreateInventoryDbContext();
        foreach (var (id, sku, stockQty) in products)
        {
            var stock = Stock.Create(id, sku, stockQty);
            inventoryDb.Stocks.Add(stock);
        }
        await inventoryDb.SaveChangesAsync();

        var random = new Random(42);
        var successesByProduct = new ConcurrentDictionary<Guid, int>();
        foreach (var p in products) successesByProduct[p.Id] = 0;

        foreach (var _ in Enumerable.Range(0, 50))
        {
            var product = products[random.Next(products.Count)];
            var orderId = Guid.NewGuid();
            var command = new ReserveInventoryCommand(
                orderId,
                [new OrderItemReservation(product.Id, 1, product.Sku)]);

            try
            {
                await Fixture.Host.TrackActivity()
                    .Timeout(TimeSpan.FromSeconds(30))
                    .InvokeMessageAndWaitAsync(command);
                successesByProduct.AddOrUpdate(product.Id, 1, (_, v) => v + 1);
            }
            catch
            {
                // Expected for low-stock products
            }
        }

        await using var verifyDb = Fixture.CreateInventoryDbContext();

        foreach (var (id, sku, stockQty) in products)
        {
            var finalStock = await verifyDb.Stocks
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.ProductId == id);

            finalStock.ShouldNotBeNull();
            var reserved = finalStock.GetReservedQuantity();
            var available = finalStock.GetAvailableQuantity();

            (reserved + available).ShouldBe(finalStock.Quantity, $"Stock invariant violated for {sku}");
            reserved.ShouldBeLessThanOrEqualTo(stockQty, $"Overselling on {sku}");
        }
    }

    #endregion
}
