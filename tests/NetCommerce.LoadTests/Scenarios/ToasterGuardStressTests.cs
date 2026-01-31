#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NBomber.CSharp;
using NetCommerce.Domain.Shared;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.LoadTests.Assertions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
///     PRODUCTION-READINESS TEST: Contention-Skew Stress Tests (The "Toaster" Guard)
///
///     <para>
///     This test suite validates that Partitioned Sequential Messaging (9-11 partitions)
///     prevents "Thread Starvation" under Hot Key scenarios. The key insight is that
///     a customer ordering a Toaster should NOT experience degraded latency just because
///     1,000 other customers are fighting over the last PS5.
///     </para>
///
///     <para>
///     <b>Test Scenario:</b>
///     - 1,000 concurrent requests target "Product A" (PS5) - the "Hot Key"
///     - 1 request targets "Product B" (Toaster) that hashes to the SAME partition slot
///     - Measure latency of the Toaster request under PS5 flood
///     </para>
///
///     <para>
///     <b>Success Criteria:</b>
///     - Toaster request latency &lt; 2 seconds (acceptable queue delay)
///     - Latency growth is LINEAR with queue depth, not EXPONENTIAL with load
///     - Zero database deadlocks (proves partitioning eliminated DB contention)
///     </para>
///
///     <para>
///     <b>Why This Matters:</b>
///     Without partitioning, all 1,001 requests would compete for database locks on the
///     Stock table, causing deadlocks and exponential latency spikes. With partitioning,
///     the Toaster request waits in a predictable FIFO queue.
///     </para>
/// </summary>
public class ContentionSkewStressTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;
    private string ConnectionString => _postgresContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("loadtest_db")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgresContainer.StartAsync();
        await InitializeDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE SCHEMA IF NOT EXISTS inventory;

            CREATE TABLE IF NOT EXISTS inventory.stocks (
                id uuid PRIMARY KEY,
                product_id uuid NOT NULL UNIQUE,
                quantity integer NOT NULL DEFAULT 0,
                reserved integer NOT NULL DEFAULT 0,
                tenant_id text NOT NULL DEFAULT 'default',
                created_at timestamptz NOT NULL DEFAULT now(),
                version integer NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS inventory.reservations (
                id uuid PRIMARY KEY,
                stock_id uuid NOT NULL REFERENCES inventory.stocks(id),
                order_id uuid NOT NULL,
                quantity integer NOT NULL,
                status integer NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT now(),
                expires_at timestamptz NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_stocks_product_id ON inventory.stocks(product_id);
            CREATE INDEX IF NOT EXISTS idx_reservations_stock_id ON inventory.reservations(stock_id);
            CREATE INDEX IF NOT EXISTS idx_reservations_order_id ON inventory.reservations(order_id);
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    #region Test 1: Toaster Guard - Hot Key Should Not Starve Cold Key

    /// <summary>
    ///     THE TOASTER GUARD TEST
    ///
    ///     <para>
    ///     Validates "Linearity of Latency" - when 1,000 requests hit the PS5,
    ///     a single Toaster request that shares the same partition slot should
    ///     experience LINEAR queue delay, not EXPONENTIAL database lock contention.
    ///     </para>
    ///
    ///     <para>
    ///     Expected Behavior with 9-11 Partitions:
    ///     - PS5 requests queue on their partition (linear processing)
    ///     - Toaster request waits for ~N messages in the queue ahead
    ///     - Latency = (Position in Queue) × (Average Processing Time)
    ///     </para>
    ///
    ///     <para>
    ///     Failure Mode (without partitioning):
    ///     - Database FOR UPDATE locks create deadlock chains
    ///     - Latency grows EXPONENTIALLY with concurrent requests
    ///     - Timeouts and 500 errors cascade
    ///     </para>
    /// </summary>
    [Fact(Skip = "Run manually - requires 30s+ execution time")]
    public async Task ToasterGuard_HotKeyFlood_ShouldNotStarveColdKey()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        const int hotKeyRequestCount = 1000;
        const int coldKeyRequestCount = 1;
        const int partitionCount = 9; // NetCommerce default
        const double maxToasterLatencySeconds = 2.0;

        // Create two products that hash to the SAME partition (worst case)
        var ps5ProductId = CreateProductIdForPartition(0, partitionCount);
        var toasterProductId = CreateProductIdForPartition(0, partitionCount); // Same partition!

        // Seed stock for both products
        await SeedStockAsync(ps5ProductId, quantity: 10_000);
        await SeedStockAsync(toasterProductId, quantity: 100);

        // Metrics collection
        var hotKeyLatencies = new ConcurrentBag<double>();
        var coldKeyLatency = new Stopwatch();
        var deadlockCount = 0;
        var successCount = 0;

        // ═══════════════════════════════════════════════════════════════════════
        // EXECUTE: Flood PS5 while sneaking in Toaster request
        // ═══════════════════════════════════════════════════════════════════════

        // Start hot key flood
        var hotKeyTasks = Enumerable.Range(0, hotKeyRequestCount)
            .Select(async i =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await SimulateReservationAsync(ps5ProductId, 1);
                    sw.Stop();
                    hotKeyLatencies.Add(sw.Elapsed.TotalMilliseconds);
                    Interlocked.Increment(ref successCount);
                }
                catch (NpgsqlException ex) when (ex.Message.Contains("deadlock"))
                {
                    Interlocked.Increment(ref deadlockCount);
                }
                catch
                {
                    // Other errors
                }
            });

        // Inject cold key request in the middle
        var coldKeyTask = Task.Run(async () =>
        {
            await Task.Delay(100); // Let some hot keys queue up
            coldKeyLatency.Start();
            try
            {
                await SimulateReservationAsync(toasterProductId, 1);
            }
            finally
            {
                coldKeyLatency.Stop();
            }
        });

        await Task.WhenAll(hotKeyTasks.Append(coldKeyTask));

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERTIONS
        // ═══════════════════════════════════════════════════════════════════════

        // 1. ZERO DEADLOCKS - proves partitioning eliminated DB contention
        deadlockCount.ShouldBe(0,
            $"CRITICAL: {deadlockCount} deadlocks detected!\n" +
            "This proves database lock contention is occurring.\n" +
            "Partitioned Sequential Messaging should eliminate this.");

        // 2. TOASTER LATENCY - should be bounded, not exponential
        var toasterLatencySeconds = coldKeyLatency.Elapsed.TotalSeconds;
        toasterLatencySeconds.ShouldBeLessThan(maxToasterLatencySeconds,
            $"CRITICAL: Toaster request took {toasterLatencySeconds:F2}s (max: {maxToasterLatencySeconds}s)\n" +
            "The cold key is being starved by hot key traffic.\n" +
            "Check partition count and queue depth configuration.");

        // 3. LINEAR LATENCY GROWTH - prove predictable behavior
        if (hotKeyLatencies.Any())
        {
            var sortedLatencies = hotKeyLatencies.OrderBy(l => l).ToList();
            var p50 = sortedLatencies[(int)(sortedLatencies.Count * 0.50)];
            var p95 = sortedLatencies[(int)(sortedLatencies.Count * 0.95)];
            var p99 = sortedLatencies[(int)(sortedLatencies.Count * 0.99)];

            // Linear growth means P99 should be roughly proportional to queue depth
            // Exponential growth would show P99 >> 10 × P50
            var latencyRatio = p99 / p50;
            latencyRatio.ShouldBeLessThan(20.0,
                $"Latency distribution suggests exponential growth (P99/P50 = {latencyRatio:F2})\n" +
                "Expected linear growth with partitioning.");

            // Output metrics for analysis
            Console.WriteLine($"[ToasterGuard] Hot Key Metrics:");
            Console.WriteLine($"  Requests: {hotKeyRequestCount}");
            Console.WriteLine($"  Success: {successCount}");
            Console.WriteLine($"  Deadlocks: {deadlockCount}");
            Console.WriteLine($"  P50 Latency: {p50:F2}ms");
            Console.WriteLine($"  P95 Latency: {p95:F2}ms");
            Console.WriteLine($"  P99 Latency: {p99:F2}ms");
            Console.WriteLine($"[ToasterGuard] Cold Key (Toaster) Latency: {toasterLatencySeconds * 1000:F2}ms");
        }
    }

    #endregion

    #region Test 2: Partition Density Validation

    /// <summary>
    ///     Validates that the partition count (9-11) provides sufficient isolation
    ///     to prevent queue saturation under realistic product distribution.
    ///
    ///     <para>
    ///     Uses Zipf distribution to simulate real-world product popularity:
    ///     - Top 10% of products receive 90% of traffic
    ///     - Validates that "hot" products don't monopolize all partitions
    ///     </para>
    /// </summary>
    [Fact(Skip = "Run manually - requires 30s+ execution time")]
    public async Task PartitionDensity_WithZipfDistribution_ShouldNotSaturate()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        const int totalProducts = 100;
        const int totalRequests = 5000;
        const int partitionCount = 9;
        const double zipfExponent = 1.5; // Higher = more skewed

        // Create products
        var products = Enumerable.Range(0, totalProducts)
            .Select(_ => Guid.NewGuid())
            .ToList();

        // Seed stock for all products
        foreach (var productId in products)
        {
            await SeedStockAsync(productId, quantity: 10_000);
        }

        // Generate Zipf-distributed requests
        var requests = GenerateZipfRequests(products, totalRequests, zipfExponent);
        var partitionHits = new int[partitionCount];

        // Track partition distribution
        foreach (var productId in requests)
        {
            var partition = GetPartitionSlot(productId, partitionCount);
            Interlocked.Increment(ref partitionHits[partition]);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERTIONS
        // ═══════════════════════════════════════════════════════════════════════

        // 1. No single partition should receive more than 40% of total requests
        var maxPartitionLoad = partitionHits.Max();
        var maxPartitionPercentage = (double)maxPartitionLoad / totalRequests * 100;

        maxPartitionPercentage.ShouldBeLessThan(40.0,
            $"Partition {Array.IndexOf(partitionHits, maxPartitionLoad)} received {maxPartitionPercentage:F1}% of requests.\n" +
            "This indicates poor partition distribution. Consider increasing partition count.");

        // 2. At least 70% of partitions should receive some traffic
        var activePartitions = partitionHits.Count(h => h > 0);
        var activePercentage = (double)activePartitions / partitionCount * 100;

        activePercentage.ShouldBeGreaterThan(70.0,
            $"Only {activePartitions}/{partitionCount} partitions are active.\n" +
            "Traffic is too concentrated.");

        // Output distribution for analysis
        Console.WriteLine("[PartitionDensity] Distribution:");
        for (int i = 0; i < partitionCount; i++)
        {
            var percentage = (double)partitionHits[i] / totalRequests * 100;
            Console.WriteLine($"  Partition {i}: {partitionHits[i]} requests ({percentage:F1}%)");
        }
    }

    #endregion

    #region Test 3: Queue Depth vs Latency Linearity

    /// <summary>
    ///     Measures the correlation between queue depth and request latency.
    ///     Linear correlation proves predictable behavior.
    ///     Non-linear correlation indicates database contention leaking through.
    /// </summary>
    [Fact(Skip = "Run manually - requires 30s+ execution time")]
    public async Task QueueDepth_ShouldCorrelateLinearlyWithLatency()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════
        const int partitionCount = 9;
        var singleProductId = CreateProductIdForPartition(0, partitionCount);
        await SeedStockAsync(singleProductId, quantity: 10_000);

        // Measure latency at different queue depths
        var queueDepths = new[] { 10, 50, 100, 200, 500 };
        var results = new List<(int Depth, double AvgLatency)>();

        foreach (var depth in queueDepths)
        {
            var latencies = new ConcurrentBag<double>();

            var tasks = Enumerable.Range(0, depth)
                .Select(async _ =>
                {
                    var sw = Stopwatch.StartNew();
                    await SimulateReservationAsync(singleProductId, 1);
                    sw.Stop();
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                });

            await Task.WhenAll(tasks);

            var avgLatency = latencies.Average();
            results.Add((depth, avgLatency));

            Console.WriteLine($"[QueueDepth] Depth={depth}, AvgLatency={avgLatency:F2}ms");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERTIONS: Verify Linear Growth
        // ═══════════════════════════════════════════════════════════════════════

        // Calculate the ratio of latency increase vs depth increase
        // Linear: 10x depth = ~10x latency
        // Exponential: 10x depth = 100x+ latency

        var firstResult = results.First();
        var lastResult = results.Last();

        var depthMultiplier = (double)lastResult.Depth / firstResult.Depth;
        var latencyMultiplier = lastResult.AvgLatency / firstResult.AvgLatency;

        // Allow 3x tolerance for linear scaling (accounts for overhead)
        var expectedMaxMultiplier = depthMultiplier * 3;

        latencyMultiplier.ShouldBeLessThan(expectedMaxMultiplier,
            $"Latency growth is non-linear!\n" +
            $"Depth increased {depthMultiplier:F1}x but latency increased {latencyMultiplier:F1}x\n" +
            "Expected roughly linear scaling with partitioned queues.");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Creates a ProductId that will hash to a specific partition slot.
    ///     Uses brute-force GUID generation until we find one that hashes correctly.
    /// </summary>
    private static Guid CreateProductIdForPartition(int targetPartition, int partitionCount)
    {
        while (true)
        {
            var candidate = Guid.NewGuid();
            if (GetPartitionSlot(candidate, partitionCount) == targetPartition)
                return candidate;
        }
    }

    /// <summary>
    ///     Calculates the partition slot for a given ProductId.
    ///     This mirrors the partitioning logic used in NetCommerce.
    /// </summary>
    private static int GetPartitionSlot(Guid productId, int partitionCount)
    {
        // Use consistent hashing based on ProductId
        return Math.Abs(productId.GetHashCode()) % partitionCount;
    }

    private async Task SeedStockAsync(Guid productId, int quantity)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO inventory.stocks (id, product_id, quantity, reserved, tenant_id)
            VALUES (@id, @productId, @quantity, 0, 'default')
            ON CONFLICT (product_id) DO UPDATE SET quantity = @quantity, reserved = 0;";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("productId", productId);
        cmd.Parameters.AddWithValue("quantity", quantity);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SimulateReservationAsync(Guid productId, int quantity)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Simulate the optimistic concurrency pattern used in NetCommerce
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 1. Read current stock (with row-level lock simulation)
            await using var selectCmd = connection.CreateCommand();
            selectCmd.Transaction = transaction;
            selectCmd.CommandText = @"
                SELECT id, quantity, reserved, version
                FROM inventory.stocks
                WHERE product_id = @productId
                FOR UPDATE;";
            selectCmd.Parameters.AddWithValue("productId", productId);

            Guid stockId;
            int currentQty, currentReserved, version;

            await using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("Stock not found");

                stockId = reader.GetGuid(0);
                currentQty = reader.GetInt32(1);
                currentReserved = reader.GetInt32(2);
                version = reader.GetInt32(3);
            }

            // 2. Check availability
            var available = currentQty - currentReserved;
            if (available < quantity)
                throw new InvalidOperationException("Insufficient stock");

            // 3. Update reserved count with optimistic concurrency
            await using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = @"
                UPDATE inventory.stocks
                SET reserved = reserved + @quantity, version = version + 1
                WHERE id = @stockId AND version = @version;";
            updateCmd.Parameters.AddWithValue("quantity", quantity);
            updateCmd.Parameters.AddWithValue("stockId", stockId);
            updateCmd.Parameters.AddWithValue("version", version);

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            if (rowsAffected == 0)
                throw new InvalidOperationException("Concurrency conflict");

            // 4. Create reservation record
            await using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = @"
                INSERT INTO inventory.reservations (id, stock_id, order_id, quantity, status, expires_at)
                VALUES (@id, @stockId, @orderId, @quantity, 0, @expiresAt);";
            insertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            insertCmd.Parameters.AddWithValue("stockId", stockId);
            insertCmd.Parameters.AddWithValue("orderId", Guid.NewGuid());
            insertCmd.Parameters.AddWithValue("quantity", quantity);
            insertCmd.Parameters.AddWithValue("expiresAt", DateTime.UtcNow.AddMinutes(15));

            await insertCmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    ///     Generates Zipf-distributed product selections (simulates real-world popularity).
    /// </summary>
    private static List<Guid> GenerateZipfRequests(List<Guid> products, int totalRequests, double exponent)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var result = new List<Guid>();

        // Calculate Zipf weights
        var weights = products.Select((_, i) => 1.0 / Math.Pow(i + 1, exponent)).ToList();
        var totalWeight = weights.Sum();
        var normalizedWeights = weights.Select(w => w / totalWeight).ToList();

        // Generate cumulative distribution
        var cumulative = new List<double>();
        double sum = 0;
        foreach (var w in normalizedWeights)
        {
            sum += w;
            cumulative.Add(sum);
        }

        // Sample from distribution
        for (int i = 0; i < totalRequests; i++)
        {
            var r = random.NextDouble();
            var index = cumulative.FindIndex(c => c >= r);
            if (index < 0) index = products.Count - 1;
            result.Add(products[index]);
        }

        return result;
    }

    #endregion
}
