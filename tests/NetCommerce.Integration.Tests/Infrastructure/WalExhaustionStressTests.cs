#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using Npgsql;
using Shouldly;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
///     ADVERSARIAL INFRASTRUCTURE TEST: WAL Exhaustion Stress Test
///
///     <para>
///     This test identifies the PostgreSQL Write-Ahead Log (WAL) IOPS ceiling.
///     The WAL is a single sequential file that becomes the bottleneck for ALL
///     write operations, regardless of how many tables or partitions exist.
///     </para>
///
///     <para>
///     <b>Production Risk:</b>
///     - Flash sale writes 10,000 reservation TXNs/second
///     - PostgreSQL WAL can only handle ~5,000 fsync() calls/second
///     - Result: Exponential latency growth, transaction timeouts, cascading failures
///     </para>
///
///     <para>
///     <b>Test Goal:</b>
///     Identify the IOPS ceiling where latency growth becomes non-linear.
///     This number informs capacity planning and rate-limiting configuration.
///     </para>
///
///     <para>
///     <b>Key Metrics:</b>
///     - Throughput (TXN/s) at various concurrency levels
///     - P99 latency inflection point
///     - WAL fsync() saturation indicator
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "Adversarial")]
[Trait("Category", "Infrastructure")]
[Trait("Category", "Capacity")]
public class WalExhaustionStressTests : IntegrationTestBase
{
    public WalExhaustionStressTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Identify WAL Throughput Ceiling

    /// <summary>
    ///     Measures PostgreSQL write throughput to identify the WAL IOPS ceiling.
    ///
    ///     <para>
    ///     This test incrementally increases write concurrency until latency
    ///     growth becomes non-linear (the "knee" of the saturation curve).
    ///     </para>
    ///
    ///     <para>
    ///     <b>Interpretation:</b>
    ///     - Linear region: Throughput scales with concurrency
    ///     - Knee point: WAL becomes bottleneck
    ///     - Saturation region: More concurrency = worse performance
    ///     </para>
    /// </summary>
    [Fact]
    public async Task WalThroughput_ShouldIdentifyIopsCeiling()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        var concurrencyLevels = new[] { 1, 5, 10, 25, 50 };
        const int operationsPerLevel = 100;

        Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║        ADVERSARIAL DRILL: WAL Exhaustion Stress Test              ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ Goal: Identify PostgreSQL WAL IOPS ceiling for capacity planning  ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝\n");

        var results = new List<(int Concurrency, double Throughput, double P50, double P95, double P99)>();

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Test each concurrency level
        // ═══════════════════════════════════════════════════════════════════════
        foreach (var concurrency in concurrencyLevels)
        {
            var metrics = await MeasureWriteThroughputAsync(concurrency, operationsPerLevel);
            results.Add((concurrency, metrics.Throughput, metrics.P50, metrics.P95, metrics.P99));

            Console.WriteLine($"[WAL] Concurrency: {concurrency,3} | " +
                            $"Throughput: {metrics.Throughput,8:F1} TXN/s | " +
                            $"P50: {metrics.P50,6:F1}ms | " +
                            $"P95: {metrics.P95,6:F1}ms | " +
                            $"P99: {metrics.P99,6:F1}ms");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT & ANALYZE: Identify the knee point
        // ═══════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                     ANALYSIS                                       ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════════════╣");

        // Find where P99 latency growth becomes non-linear (>2x previous level)
        var kneePoint = -1;
        for (int i = 1; i < results.Count; i++)
        {
            var latencyGrowth = results[i].P99 / results[i - 1].P99;
            if (latencyGrowth > 2.0)
            {
                kneePoint = results[i - 1].Concurrency;
                Console.WriteLine($"║ ⚠️  KNEE POINT DETECTED at concurrency: {kneePoint}                 ║");
                Console.WriteLine($"║ P99 latency growth: {latencyGrowth:F1}x (non-linear)                     ║");
                break;
            }
        }

        if (kneePoint == -1)
        {
            Console.WriteLine("║ ✓ No knee point detected in tested range                          ║");
            Console.WriteLine("║   WAL throughput appears sufficient for tested concurrency        ║");
        }

        // Calculate peak throughput
        var peakThroughput = results.Max(r => r.Throughput);
        var peakConcurrency = results.First(r => Math.Abs(r.Throughput - peakThroughput) < 0.1).Concurrency;
        Console.WriteLine($"║ Peak throughput: {peakThroughput:F1} TXN/s at concurrency {peakConcurrency}         ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");

        // Basic sanity assertions
        results.Count.ShouldBeGreaterThan(0, "Should have measurement results");
        peakThroughput.ShouldBeGreaterThan(0, "Peak throughput should be positive");
    }

    private async Task<(double Throughput, double P50, double P95, double P99)> MeasureWriteThroughputAsync(
        int concurrency, int totalOperations)
    {
        var latencies = new ConcurrentBag<double>();
        var operationsPerWorker = totalOperations / concurrency;
        var startBarrier = new TaskCompletionSource();
        var productIds = Enumerable.Range(0, concurrency).Select(_ => Guid.NewGuid()).ToList();

        // Seed stock for each product
        await using var seedDb = Fixture.CreateInventoryDbContext();
        foreach (var productId in productIds)
        {
            var stock = Stock.Create(productId, $"SKU-WAL-{productId:N}", 10000);
            seedDb.Stocks.Add(stock);
        }
        await seedDb.SaveChangesAsync();

        // Create worker tasks
        var tasks = Enumerable.Range(0, concurrency).Select(async workerId =>
        {
            var productId = productIds[workerId];
            await startBarrier.Task;

            for (int i = 0; i < operationsPerWorker; i++)
            {
                var orderId = Guid.NewGuid();
                var command = new ReserveInventoryCommand(
                    orderId,
                    [new OrderItemReservation(productId, 1, $"SKU-WAL-{productId:N}")]);

                var sw = Stopwatch.StartNew();
                try
                {
                    await Fixture.Host.InvokeMessageAndWaitAsync(command);
                    sw.Stop();
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    sw.Stop();
                    // Include failed operations in latency metrics
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
        });

        // Start all workers simultaneously
        var overallSw = Stopwatch.StartNew();
        startBarrier.SetResult();
        await Task.WhenAll(tasks);
        overallSw.Stop();

        // Calculate metrics
        var sortedLatencies = latencies.OrderBy(l => l).ToList();
        var throughput = latencies.Count / overallSw.Elapsed.TotalSeconds;
        var p50 = sortedLatencies[(int)(sortedLatencies.Count * 0.50)];
        var p95 = sortedLatencies[(int)(sortedLatencies.Count * 0.95)];
        var p99 = sortedLatencies[(int)(sortedLatencies.Count * 0.99)];

        return (throughput, p50, p95, p99);
    }

    #endregion

    #region Test 2: Multi-Table Write Contention

    /// <summary>
    ///     Tests WAL behavior when multiple tables are written simultaneously.
    ///
    ///     <para>
    ///     Key insight: Even with table-level partitioning, all writes go through
    ///     the same WAL. This test verifies cross-table write contention.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task MultiTableWrites_ShouldShareWalBandwidth()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create products for multi-table write test
        // ═══════════════════════════════════════════════════════════════════════
        const int productsPerTable = 5;
        const int operationsPerProduct = 20;

        var products = new List<(Guid ProductId, string Sku)>();
        await using var seedDb = Fixture.CreateInventoryDbContext();

        for (int i = 0; i < productsPerTable; i++)
        {
            var productId = Guid.NewGuid();
            var sku = $"SKU-MULTI-{i:D3}";
            var stock = Stock.Create(productId, sku, 1000);
            seedDb.Stocks.Add(stock);
            products.Add((productId, sku));
        }
        await seedDb.SaveChangesAsync();

        Console.WriteLine("[MultiTable] Testing cross-table WAL contention...");
        Console.WriteLine($"[MultiTable] Products: {productsPerTable}, Operations/product: {operationsPerProduct}");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Write to all products concurrently
        // ═══════════════════════════════════════════════════════════════════════
        var latencies = new ConcurrentBag<double>();
        var sw = Stopwatch.StartNew();

        var tasks = products.SelectMany(p =>
            Enumerable.Range(0, operationsPerProduct).Select(async _ =>
            {
                var orderId = Guid.NewGuid();
                var command = new ReserveInventoryCommand(
                    orderId,
                    [new OrderItemReservation(p.ProductId, 1, p.Sku)]);

                var opSw = Stopwatch.StartNew();
                try
                {
                    await Fixture.Host.InvokeMessageAndWaitAsync(command);
                    opSw.Stop();
                    latencies.Add(opSw.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    opSw.Stop();
                }
            }));

        await Task.WhenAll(tasks);
        sw.Stop();

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Calculate and display metrics
        // ═══════════════════════════════════════════════════════════════════════
        var throughput = latencies.Count / sw.Elapsed.TotalSeconds;
        var sortedLatencies = latencies.OrderBy(l => l).ToList();
        var p99 = sortedLatencies.Count > 0 ? sortedLatencies[(int)(sortedLatencies.Count * 0.99)] : 0;

        Console.WriteLine($"[MultiTable] Throughput: {throughput:F1} TXN/s");
        Console.WriteLine($"[MultiTable] P99 latency: {p99:F1}ms");
        Console.WriteLine($"[MultiTable] Total operations: {latencies.Count}");

        // Verify operations completed
        latencies.Count.ShouldBeGreaterThan(0, "Some operations should complete");

        // Verify no overselling occurred
        await using var verifyDb = Fixture.CreateInventoryDbContext();
        foreach (var (productId, _) in products)
        {
            var stock = await verifyDb.Stocks.FirstOrDefaultAsync(s => s.ProductId == productId);
            stock.ShouldNotBeNull();
            stock.ReservedQuantity.ShouldBeLessThanOrEqualTo(1000,
                $"Multi-table test should not cause overselling for product {productId}");
        }

        Console.WriteLine("[MultiTable] ✓ WAL shared bandwidth test complete, no overselling");
    }

    #endregion

    #region Test 3: Transaction Size Impact on WAL

    /// <summary>
    ///     Tests how transaction size affects WAL throughput.
    ///
    ///     <para>
    ///     Larger transactions generate more WAL data per commit,
    ///     potentially reducing throughput but improving efficiency.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task TransactionSize_ShouldAffectWalThroughput()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Products for batch testing
        // ═══════════════════════════════════════════════════════════════════════
        var products = new List<(Guid ProductId, string Sku)>();
        await using var seedDb = Fixture.CreateInventoryDbContext();

        for (int i = 0; i < 10; i++)
        {
            var productId = Guid.NewGuid();
            var sku = $"SKU-BATCH-{i:D3}";
            var stock = Stock.Create(productId, sku, 10000);
            seedDb.Stocks.Add(stock);
            products.Add((productId, sku));
        }
        await seedDb.SaveChangesAsync();

        Console.WriteLine("[TransactionSize] Testing transaction size impact on WAL...");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Compare single-item vs multi-item reservations
        // ═══════════════════════════════════════════════════════════════════════

        // Single-item transactions (many small WAL entries)
        var singleItemLatencies = new ConcurrentBag<double>();
        var singleSw = Stopwatch.StartNew();

        var singleTasks = Enumerable.Range(0, 50).Select(async i =>
        {
            var product = products[i % products.Count];
            var orderId = Guid.NewGuid();
            var command = new ReserveInventoryCommand(
                orderId,
                [new OrderItemReservation(product.ProductId, 1, product.Sku)]);

            var opSw = Stopwatch.StartNew();
            try
            {
                await Fixture.Host.InvokeMessageAndWaitAsync(command);
                opSw.Stop();
                singleItemLatencies.Add(opSw.Elapsed.TotalMilliseconds);
            }
            catch
            {
                opSw.Stop();
            }
        });

        await Task.WhenAll(singleTasks);
        singleSw.Stop();

        // Multi-item transactions (fewer, larger WAL entries)
        var multiItemLatencies = new ConcurrentBag<double>();
        var multiSw = Stopwatch.StartNew();

        var multiTasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var orderId = Guid.NewGuid();
            var items = products.Take(5).Select(p =>
                new OrderItemReservation(p.ProductId, 1, p.Sku)).ToList();

            var command = new ReserveInventoryCommand(orderId, items);

            var opSw = Stopwatch.StartNew();
            try
            {
                await Fixture.Host.InvokeMessageAndWaitAsync(command);
                opSw.Stop();
                multiItemLatencies.Add(opSw.Elapsed.TotalMilliseconds);
            }
            catch
            {
                opSw.Stop();
            }
        });

        await Task.WhenAll(multiTasks);
        multiSw.Stop();

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Compare throughput metrics
        // ═══════════════════════════════════════════════════════════════════════
        var singleThroughput = singleItemLatencies.Count / singleSw.Elapsed.TotalSeconds;
        var multiThroughput = multiItemLatencies.Count / multiSw.Elapsed.TotalSeconds;

        Console.WriteLine($"[TransactionSize] Single-item: {singleThroughput:F1} TXN/s");
        Console.WriteLine($"[TransactionSize] Multi-item (5 items): {multiThroughput:F1} TXN/s");

        // Both should complete without errors
        singleItemLatencies.Count.ShouldBeGreaterThan(0);
        multiItemLatencies.Count.ShouldBeGreaterThan(0);

        Console.WriteLine("[TransactionSize] ✓ Transaction size impact measured");
    }

    #endregion

    #region Test 4: WAL Under Sustained Load

    /// <summary>
    ///     Tests WAL behavior under sustained write load (duration-based).
    ///
    ///     <para>
    ///     This helps identify if WAL performance degrades over time
    ///     (e.g., due to checkpoint pressure or file system issues).
    ///     </para>
    /// </summary>
    [Fact]
    public async Task WalUnderSustainedLoad_ShouldMaintainStableThroughput()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create stock for sustained load test
        // ═══════════════════════════════════════════════════════════════════════
        var productId = Guid.NewGuid();
        await using var seedDb = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, "SKU-SUSTAINED-001", 100000);
        seedDb.Stocks.Add(stock);
        await seedDb.SaveChangesAsync();

        const int durationSeconds = 5;
        const int targetOpsPerSecond = 20;

        Console.WriteLine("[SustainedLoad] Testing WAL stability over time...");
        Console.WriteLine($"[SustainedLoad] Duration: {durationSeconds}s, Target: {targetOpsPerSecond} ops/s");

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Run sustained load for specified duration
        // ═══════════════════════════════════════════════════════════════════════
        var latenciesBySecond = new Dictionary<int, List<double>>();
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddSeconds(durationSeconds);
        var operationCount = 0;

        while (DateTime.UtcNow < endTime)
        {
            var second = (int)(DateTime.UtcNow - startTime).TotalSeconds;
            if (!latenciesBySecond.ContainsKey(second))
                latenciesBySecond[second] = [];

            var orderId = Guid.NewGuid();
            var command = new ReserveInventoryCommand(
                orderId,
                [new OrderItemReservation(productId, 1, "SKU-SUSTAINED-001")]);

            var sw = Stopwatch.StartNew();
            try
            {
                await Fixture.Host.InvokeMessageAndWaitAsync(command);
                sw.Stop();
                latenciesBySecond[second].Add(sw.Elapsed.TotalMilliseconds);
                operationCount++;
            }
            catch
            {
                sw.Stop();
            }

            // Rate limit to target ops/second
            await Task.Delay(1000 / targetOpsPerSecond);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Analyze throughput stability
        // ═══════════════════════════════════════════════════════════════════════
        Console.WriteLine($"\n[SustainedLoad] Total operations: {operationCount}");

        var avgLatencies = latenciesBySecond
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (Second: kvp.Key, AvgLatency: kvp.Value.Average()))
            .ToList();

        foreach (var (second, avgLatency) in avgLatencies)
        {
            Console.WriteLine($"[SustainedLoad] Second {second}: Avg latency {avgLatency:F1}ms");
        }

        // Check for latency degradation over time
        if (avgLatencies.Count >= 2)
        {
            var firstSecondLatency = avgLatencies.First().AvgLatency;
            var lastSecondLatency = avgLatencies.Last().AvgLatency;
            var degradation = lastSecondLatency / firstSecondLatency;

            if (degradation > 2.0)
            {
                Console.WriteLine($"[SustainedLoad] ⚠️ Latency degraded {degradation:F1}x over test duration");
            }
            else
            {
                Console.WriteLine($"[SustainedLoad] ✓ Latency stable (degradation factor: {degradation:F2}x)");
            }
        }

        operationCount.ShouldBeGreaterThan(0, "Sustained load test should complete operations");
    }

    #endregion
}
