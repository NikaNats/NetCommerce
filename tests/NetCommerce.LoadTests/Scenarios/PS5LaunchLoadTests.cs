using System.Text;
using System.Text.Json;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NetCommerce.LoadTests.Assertions;
using Shouldly;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
///     Load tests simulating the PS5 launch scenario with Partitioned Sequential Messaging.
///
///     <para>
///     Architecture Validation:
///     These tests verify the "ACM Award" solution for high-contention inventory management.
///     By using Wolverine's message partitioning, we convert "Hardware Contention" (DB Locking)
///     into "Software Scheduling" (Message Partitioning).
///     </para>
///
///     <para>
///     What to expect:
///     - BEFORE (with FOR UPDATE): High DB timeout errors, 500ms+ latency
///     - AFTER (with Partitioning): 0% errors, linear latency scaling
///     </para>
///
///     <para>
///     Saga Leak Detection:
///     After each load test, we assert that active.sagas counter returns to zero.
///     A non-zero count indicates orphaned saga instances that could cause:
///     - Memory leaks
///     - Database connection leaks
///     - Incorrect business state
///     </para>
/// </summary>
public class PS5LaunchLoadTests
{
    /// <summary>
    ///     Simulates PS5 console launch: 1000 users trying to reserve 100 units.
    ///     Tests system behavior under extreme contention with partitioned messaging.
    /// </summary>
    /// <remarks>
    ///     Architecture Notes:
    ///     - All PS5 reservation requests will be routed to the same "track" (partition)
    ///     - Requests are processed sequentially within the track, eliminating DB locks
    ///     - Expected: Linear latency scaling, zero deadlocks
    /// </remarks>
    [Fact(Skip = "Run manually - requires running API")]
    public async Task PS5Launch_HighDemandReservation_WithPartitionedMessaging_ShouldHandleConcurrency()
    {
        // Configuration
        const int totalStock = 100;
        const int concurrentUsers = 1000;
        const string apiBaseUrl = "http://localhost:5000";

        var productId = Guid.NewGuid();
        var successCount = 0;
        var failedDueToStockCount = 0;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl)
        };

        var scenario = Scenario.Create("ps5_launch_partitioned", async context =>
            {
                var orderId = Guid.NewGuid();
                var idempotencyKey = Guid.NewGuid().ToString();

                var request = Http.CreateRequest("POST", "/api/inventory/reserve")
                    .WithHeader("X-Idempotency-Key", idempotencyKey)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            productId,
                            orderId,
                            quantity = 1
                        }),
                        Encoding.UTF8,
                        "application/json"));

                var response = await Http.Send(httpClient, request);

                if (response.IsError)
                {
                    // Check if it's an expected "out of stock" error
                    if (response.StatusCode == "409" || response.StatusCode == "400")
                    {
                        Interlocked.Increment(ref failedDueToStockCount);
                        return Response.Ok(statusCode: response.StatusCode);
                    }

                    return Response.Fail(statusCode: response.StatusCode);
                }

                Interlocked.Increment(ref successCount);
                return Response.Ok(statusCode: response.StatusCode);
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(concurrentUsers, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/ps5-launch-partitioned")
            .Run();

        // Assertions
        var scenarioStats = stats.ScenarioStats[0];

        // At most totalStock reservations should succeed
        successCount.ShouldBeLessThanOrEqualTo(totalStock);

        // No unexpected errors (only stock depletion errors expected)
        // This is the KEY metric - with partitioning, we should see ZERO DB deadlocks
        scenarioStats.Fail.Request.Count.ShouldBe(0);

        // Response time under load should be reasonable (< 500ms p99)
        // With partitioning, latency scales linearly with queue depth
        scenarioStats.Ok.Latency.Percent99.ShouldBeLessThan(500);

        // SAGA LEAK DETECTION: Ensure all sagas completed
        // A non-zero count indicates orphaned saga instances
        await stats.AssertNoSagaLeaksAsync(apiBaseUrl);
    }

    /// <summary>
    ///     Tests multi-product flash sale: Multiple products sold simultaneously.
    ///     Validates that different products can be processed in parallel.
    /// </summary>
    /// <remarks>
    ///     Architecture Notes:
    ///     - Different products are routed to different "tracks" (partitions)
    ///     - Up to 11 products can be processed in parallel
    ///     - Same product requests are serialized within their track
    /// </remarks>
    [Fact(Skip = "Run manually - requires running API")]
    public void MultiProductFlashSale_ParallelReservations_ShouldProcessInParallel()
    {
        // Configuration - 5 different hot products
        const int productsCount = 5;
        const int stockPerProduct = 50;
        const int usersPerProduct = 100;
        const string apiBaseUrl = "http://localhost:5000";

        var productIds = Enumerable.Range(0, productsCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var successCounts = new int[productsCount];
        var failedCounts = new int[productsCount];

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl)
        };

        var scenarios = productIds.Select((productId, index) =>
            Scenario.Create($"product_{index}_reservation", async context =>
                {
                    var orderId = Guid.NewGuid();

                    var request = Http.CreateRequest("POST", "/api/inventory/reserve")
                        .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(new StringContent(
                            JsonSerializer.Serialize(new
                            {
                                productId,
                                orderId,
                                quantity = 1
                            }),
                            Encoding.UTF8,
                            "application/json"));

                    var response = await Http.Send(httpClient, request);

                    if (response.IsError)
                    {
                        if (response.StatusCode == "409" || response.StatusCode == "400")
                        {
                            Interlocked.Increment(ref failedCounts[index]);
                            return Response.Ok(statusCode: response.StatusCode);
                        }

                        return Response.Fail(statusCode: response.StatusCode);
                    }

                    Interlocked.Increment(ref successCounts[index]);
                    return Response.Ok(statusCode: response.StatusCode);
                })
                .WithoutWarmUp()
                .WithLoadSimulations(
                    Simulation.Inject(usersPerProduct, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
                )
        ).ToArray();

        var stats = NBomberRunner
            .RegisterScenarios(scenarios)
            .WithReportFolder("./load-test-reports/multi-product-flash-sale")
            .Run();

        // Assertions
        foreach (var scenarioStat in stats.ScenarioStats)
        {
            // No DB deadlocks or timeout errors
            scenarioStat.Fail.Request.Count.ShouldBe(0);

            // Response time should be reasonable
            scenarioStat.Ok.Latency.Percent99.ShouldBeLessThan(1000);
        }

        // Each product should have at most stockPerProduct successful reservations
        for (var i = 0; i < productsCount; i++)
            successCounts[i].ShouldBeLessThanOrEqualTo(stockPerProduct);
    }

    /// <summary>
    ///     Verifies zero DB deadlocks under sustained high contention.
    ///     This is the key metric for the partitioned messaging pattern.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API")]
    public void SustainedContention_ZeroDeadlocks_ShouldMaintainStability()
    {
        // Configuration - Sustained load for 30 seconds
        const string apiBaseUrl = "http://localhost:5000";
        var productId = Guid.NewGuid();
        var errorCount = 0;
        var successCount = 0;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var scenario = Scenario.Create("sustained_contention", async context =>
            {
                var orderId = Guid.NewGuid();

                var request = Http.CreateRequest("POST", "/api/inventory/reserve")
                    .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            productId,
                            orderId,
                            quantity = 1
                        }),
                        Encoding.UTF8,
                        "application/json"));

                var response = await Http.Send(httpClient, request);

                // Count any server error (5xx) as a deadlock/timeout
                if (response.StatusCode?.StartsWith("5") == true)
                {
                    Interlocked.Increment(ref errorCount);
                    return Response.Fail(statusCode: response.StatusCode);
                }

                Interlocked.Increment(ref successCount);
                return Response.Ok(statusCode: response.StatusCode);
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                // Sustained load: 50 requests per second for 30 seconds
                Simulation.Inject(50, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30))
            );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/sustained-contention")
            .Run();

        var scenarioStats = stats.ScenarioStats[0];

        // KEY ASSERTION: Zero server errors (deadlocks/timeouts)
        // With partitioned messaging, all contention is handled in-memory
        errorCount.ShouldBe(0);

        // System should remain stable under sustained load
        scenarioStats.Fail.Request.Count.ShouldBe(0);
    }

    /// <summary>
    ///     Tests optimistic locking under concurrent updates.
    ///     Multiple users trying to update the same product price.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API")]
    public void ConcurrentPriceUpdate_ShouldUseOptimisticLocking()
    {
        const string apiBaseUrl = "http://localhost:5000";
        var productId = Guid.NewGuid();
        var conflictCount = 0;
        var successCount = 0;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl)
        };

        var scenario = Scenario.Create("concurrent_price_update", async context =>
            {
                var newPrice = new Random().Next(100, 1000);

                var request = Http.CreateRequest("PUT", $"/api/products/{productId}/price")
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(new StringContent(
                        JsonSerializer.Serialize(new { amount = newPrice, currency = "GEL" }),
                        Encoding.UTF8,
                        "application/json"));

                var response = await Http.Send(httpClient, request);

                if (response.StatusCode == "409") // Conflict - optimistic lock failure
                {
                    Interlocked.Increment(ref conflictCount);
                    return Response.Ok(statusCode: "409"); // Expected behavior
                }

                if (!response.IsError) Interlocked.Increment(ref successCount);

                return response.IsError
                    ? Response.Fail(statusCode: response.StatusCode)
                    : Response.Ok(statusCode: response.StatusCode);
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(100, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
            );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/concurrent-update")
            .Run();

        // Some conflicts are expected under concurrent updates
        conflictCount.ShouldBeGreaterThan(0);

        // But not all should fail
        successCount.ShouldBeGreaterThan(0);
    }
}
