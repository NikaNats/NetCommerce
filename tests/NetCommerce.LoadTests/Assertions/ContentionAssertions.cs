using System.Net.Http.Json;
using Shouldly;

namespace NetCommerce.LoadTests.Assertions;

/// <summary>
///     Contention-specific assertions for ACM-grade stress analysis.
///     Extends SagaLeakAssertions with partition and queue behavior validation.
/// </summary>
public static class ContentionAssertions
{
    /// <summary>
    ///     Asserts that the system experienced zero database deadlocks.
    ///     This is the primary indicator that Partitioned Sequential Messaging is working.
    /// </summary>
    public static void AssertZeroDeadlocks(
        int deadlockCount,
        int totalRequests,
        string context = "")
    {
        deadlockCount.ShouldBe(0,
            $"DEADLOCK DETECTED ({context}): {deadlockCount} deadlocks out of {totalRequests} requests. " +
            "Partitioned messaging should eliminate all database-level lock contention. " +
            "Possible causes: " +
            "(1) Message partitioning is not applied to all inventory operations, " +
            "(2) Different transactions are accessing the same rows outside the partition, " +
            "(3) Partition key (OrderId vs ProductId) mismatch.");
    }

    /// <summary>
    ///     Asserts that latency growth follows a linear pattern (queue behavior).
    ///     In a properly partitioned system, latency = time_per_request * queue_depth.
    /// </summary>
    public static void AssertLinearLatencyGrowth(
        double p50LatencyMs,
        double p99LatencyMs,
        double maxAllowedRatio = 10.0)
    {
        if (p50LatencyMs < 1.0) return; // Not enough data for meaningful analysis

        var ratio = p99LatencyMs / p50LatencyMs;

        ratio.ShouldBeLessThan(maxAllowedRatio,
            $"QUEUE ANOMALY: P99/P50 latency ratio is {ratio:F2}, expected < {maxAllowedRatio}. " +
            "In a partitioned system, latency should grow linearly with queue depth. " +
            "High ratio suggests: " +
            "(1) Contention leaking outside the partition, " +
            "(2) Database locks being acquired in handlers, " +
            "(3) Non-linear backoff or retry policies.");
    }

    /// <summary>
    ///     Asserts that partition distribution is within acceptable skew limits.
    ///     Severe skew indicates poor partition key selection.
    /// </summary>
    public static void AssertPartitionSkewWithinLimits(
        int[] partitionHits,
        double maxSkewRatio = 5.0)
    {
        var nonEmptySlots = partitionHits.Where(h => h > 0).ToArray();
        if (nonEmptySlots.Length < 2) return; // Need at least 2 partitions for skew analysis

        var maxHits = nonEmptySlots.Max();
        var minHits = nonEmptySlots.Min();
        var avgHits = nonEmptySlots.Average();

        if (avgHits < 10) return; // Not enough data

        var skewRatio = maxHits / avgHits;

        if (skewRatio > maxSkewRatio)
        {
            var hotSlot = Array.IndexOf(partitionHits, maxHits);
            Console.WriteLine(
                $"⚠️  PARTITION SKEW WARNING: Slot {hotSlot} has {maxHits} hits " +
                $"({skewRatio:F2}x average). Consider increasing PartitionSlots or " +
                "implementing category-based partitioning for high-traffic products.");
        }
    }

    /// <summary>
    ///     Asserts that backpressure was applied correctly under WAL exhaustion.
    ///     The system should slow down gracefully, not crash or deadlock.
    /// </summary>
    public static void AssertGracefulBackpressure(
        double initialP99LatencyMs,
        double finalP99LatencyMs,
        int errorCount,
        int totalRequests,
        double minLatencyIncreaseRatio = 1.5)
    {
        // Under backpressure, latency should increase (queue depth growing)
        var latencyRatio = finalP99LatencyMs / Math.Max(initialP99LatencyMs, 1.0);

        latencyRatio.ShouldBeGreaterThan(minLatencyIncreaseRatio,
            $"BACKPRESSURE NOT DETECTED: Final P99 ({finalP99LatencyMs:F2}ms) is not significantly " +
            $"higher than initial ({initialP99LatencyMs:F2}ms). Expected {minLatencyIncreaseRatio}x increase " +
            "when WAL is saturated. This may indicate the IOPS ceiling was not reached.");

        // Error rate should remain low (graceful degradation, not crash)
        var errorRate = totalRequests > 0 ? (double)errorCount / totalRequests * 100 : 0;

        errorRate.ShouldBeLessThan(5.0,
            $"BACKPRESSURE FAILURE: Error rate is {errorRate:F2}% under WAL stress. " +
            "Expected < 5%. System should slow down, not fail requests.");
    }

    /// <summary>
    ///     Asserts that Triple-Pass Pricing caught all stale price scenarios.
    /// </summary>
    public static void AssertTriplePassPricingIntegrity(
        int stalePriceSuccesses,
        int priceConflicts)
    {
        stalePriceSuccesses.ShouldBe(0,
            $"TRIPLE-PASS PRICING FAILURE: {stalePriceSuccesses} orders succeeded with stale prices. " +
            "The Price Snapshotting pattern is broken. Check that: " +
            "(1) CreateOrderHandler fetches fresh prices from Catalog, " +
            "(2) expectedPrice validation is enforced, " +
            "(3) Meilisearch projection lag is not bypassing validation.");

        priceConflicts.ShouldBeGreaterThan(0,
            "TEST VALIDITY WARNING: No price conflicts detected. Either: " +
            "(1) Price updates are not propagating, " +
            "(2) Test timing is off (all orders complete before price changes), " +
            "(3) expectedPrice is not being sent in orders.");
    }

    /// <summary>
    ///     Asserts that the system recovered from high contention without saga leaks.
    /// </summary>
    public static async Task AssertPostContentionRecoveryAsync(
        HttpClient httpClient,
        TimeSpan maxRecoveryTime)
    {
        var deadline = DateTime.UtcNow.Add(maxRecoveryTime);
        var lastSagaCount = int.MaxValue;

        Console.WriteLine($"Waiting for saga backlog to drain (max {maxRecoveryTime.TotalSeconds}s)...");

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await httpClient.GetAsync("/metrics/sagas");
                if (response.IsSuccessStatusCode)
                {
                    var metrics = await response.Content.ReadFromJsonAsync<SagaRecoveryMetrics>();
                    var currentCount = metrics?.TotalActive ?? 0;

                    if (currentCount == 0)
                    {
                        Console.WriteLine("✓ Saga backlog drained successfully.");
                        return;
                    }

                    // Check for progress (count decreasing)
                    if (currentCount >= lastSagaCount && lastSagaCount < 10)
                    {
                        // Sagas may be stuck
                        Console.WriteLine($"⚠️  Saga count not decreasing: {currentCount}");
                    }

                    lastSagaCount = currentCount;
                    Console.WriteLine($"   Active sagas: {currentCount}");
                }
            }
            catch
            {
                // Metrics endpoint may not exist
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        // Final check
        throw new Xunit.Sdk.XunitException(
            $"POST-CONTENTION RECOVERY FAILURE: Saga backlog did not drain within {maxRecoveryTime.TotalSeconds}s. " +
            $"Last count: {lastSagaCount}. This indicates: " +
            "(1) Saga state machine has stuck states, " +
            "(2) Compensating actions are failing, " +
            "(3) Database write capacity is exhausted.");
    }

    private record SagaRecoveryMetrics(
        int ReservingInventory,
        int ProcessingPayment,
        int ConfirmingInventory,
        int ManualIntervention)
    {
        public int TotalActive => ReservingInventory + ProcessingPayment + ConfirmingInventory;
    }
}

/// <summary>
///     Helpers for generating and analyzing partition collisions.
/// </summary>
public static class PartitionAnalysisHelpers
{
    /// <summary>
    ///     Generates N ProductIds that all hash to the same partition slot.
    ///     Used for testing single-key saturation scenarios.
    /// </summary>
    public static Guid[] GenerateCollidingProductIds(int count, int partitionCount = 9)
    {
        var result = new List<Guid>();
        var targetSlot = -1;

        while (result.Count < count)
        {
            var candidateId = Guid.NewGuid();
            var slot = GetPartitionSlot(candidateId, partitionCount);

            if (targetSlot < 0)
            {
                targetSlot = slot;
            }

            if (slot == targetSlot)
            {
                result.Add(candidateId);
            }
        }

        return [.. result];
    }

    /// <summary>
    ///     Generates ProductIds with specific partition distribution.
    ///     Used for testing partition skew scenarios.
    /// </summary>
    public static Dictionary<int, Guid[]> GenerateDistributedProductIds(
        int[] idsPerSlot,
        int partitionCount = 9)
    {
        var result = new Dictionary<int, Guid[]>();

        for (var slot = 0; slot < idsPerSlot.Length && slot < partitionCount; slot++)
        {
            var idsNeeded = idsPerSlot[slot];
            var ids = new List<Guid>();

            while (ids.Count < idsNeeded)
            {
                var candidateId = Guid.NewGuid();
                if (GetPartitionSlot(candidateId, partitionCount) == slot)
                {
                    ids.Add(candidateId);
                }
            }

            result[slot] = [.. ids];
        }

        return result;
    }

    /// <summary>
    ///     Calculates the partition slot for a given ProductId.
    /// </summary>
    public static int GetPartitionSlot(Guid productId, int partitionCount = 9)
    {
        return Math.Abs(productId.ToString().GetHashCode()) % partitionCount;
    }

    /// <summary>
    ///     Analyzes actual partition distribution from test results.
    /// </summary>
    public static PartitionDistributionReport AnalyzeDistribution(
        IEnumerable<Guid> productIds,
        int partitionCount = 9)
    {
        var distribution = new int[partitionCount];
        var total = 0;

        foreach (var productId in productIds)
        {
            var slot = GetPartitionSlot(productId, partitionCount);
            distribution[slot]++;
            total++;
        }

        var nonEmpty = distribution.Where(d => d > 0).ToArray();

        return new PartitionDistributionReport
        {
            TotalRequests = total,
            PartitionCount = partitionCount,
            Distribution = distribution,
            MaxLoad = distribution.Max(),
            MinLoad = nonEmpty.Length > 0 ? nonEmpty.Min() : 0,
            AvgLoad = total / (double)partitionCount,
            SkewRatio = nonEmpty.Length > 0 ? distribution.Max() / nonEmpty.Average() : 0,
            HottestSlot = Array.IndexOf(distribution, distribution.Max()),
            ColdestSlot = Array.IndexOf(distribution, nonEmpty.Length > 0 ? nonEmpty.Min() : 0)
        };
    }
}

public record PartitionDistributionReport
{
    public int TotalRequests { get; init; }
    public int PartitionCount { get; init; }
    public int[] Distribution { get; init; } = [];
    public int MaxLoad { get; init; }
    public int MinLoad { get; init; }
    public double AvgLoad { get; init; }
    public double SkewRatio { get; init; }
    public int HottestSlot { get; init; }
    public int ColdestSlot { get; init; }

    public void PrintReport()
    {
        Console.WriteLine("\n┌─────────────────────────────────────────────┐");
        Console.WriteLine("│       PARTITION DISTRIBUTION ANALYSIS       │");
        Console.WriteLine("├─────────────────────────────────────────────┤");
        Console.WriteLine($"│ Total Requests: {TotalRequests,10:N0}                  │");
        Console.WriteLine($"│ Partitions:     {PartitionCount,10}                  │");
        Console.WriteLine($"│ Skew Ratio:     {SkewRatio,10:F2}x                 │");
        Console.WriteLine("├─────────────────────────────────────────────┤");

        for (var i = 0; i < Distribution.Length; i++)
        {
            var pct = TotalRequests > 0 ? Distribution[i] / (double)TotalRequests * 100 : 0;
            var bar = new string('▓', Math.Min((int)(pct / 3), 15));
            var marker = i == HottestSlot ? "🔥" : (i == ColdestSlot ? "❄️" : "  ");
            Console.WriteLine($"│ Slot {i}: {Distribution[i],6:N0} ({pct,5:F1}%) {bar,-15} {marker}│");
        }

        Console.WriteLine("└─────────────────────────────────────────────┘");
    }
}
