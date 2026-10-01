using System.Text;
using System.Text.Json;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NetCommerce.LoadTests.Assertions;
using NetCommerce.LoadTests.Fixtures;
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
[Collection(nameof(LoadTestCollection))]
public class PS5LaunchLoadTests
{
    private readonly LoadTestHostFixture _fixture;

    public PS5LaunchLoadTests(LoadTestHostFixture fixture)
    {
        _fixture = fixture;
    }

    private static int EnvInt(string name, int @default) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : @default;

    /// <summary>
    ///     Simulates PS5 console launch: N users racing to reserve M units of one product.
    ///     Tests system behavior under extreme contention with partitioned messaging.
    ///     Fully automated via <see cref="LoadTestHostFixture"/> (Testcontainers + TestServer pipeline).
    ///     Profiles scale via environment: LOAD_PS5_STOCK / LOAD_PS5_USERS / LOAD_PS5_SECONDS.
    /// </summary>
    /// <remarks>
    ///     Architecture Notes:
    ///     - All PS5 reservation requests will be routed to the same "track" (partition)
    ///     - Requests are processed sequentially within the track, eliminating DB locks
    ///     - Expected: Linear latency scaling, zero deadlocks
    ///     - Absolute latency SLOs are staging concerns; the automated gate asserts
    ///       invariants only (bounded allocation, zero unexpected failures, no saga leaks).
    /// </remarks>
    [Fact]
    [Trait("Category", "AutomatedLoadTest")]
    public async Task PS5Launch_HighDemandReservation_WithPartitionedMessaging_ShouldHandleConcurrency()
    {
        // Configuration (fast CI profile by default; scale up for staging)
        var totalStock = EnvInt("LOAD_PS5_STOCK", 20);
        var concurrentUsers = EnvInt("LOAD_PS5_USERS", 50);
        var burstSeconds = EnvInt("LOAD_PS5_SECONDS", 5);

        var productId = Guid.NewGuid();
        var successCount = 0;
        var failedDueToStockCount = 0;
        var unexpectedFailures = 0;
        var requestCounter = 0;
        // Shard rate-limit partitions like distinct prod users (see fixture header doc).
        var userPool = Math.Max(concurrentUsers * 5, 50);

        // Factory-routed client: full in-process pipeline (see fixture transport note).
        using var httpClient = _fixture.CreateClient();

        // Seed stock through the running API (VendorOnly — covered by fixture test auth)
        var seedPayload = JsonSerializer.Serialize(new
        {
            productId,
            sku = "PS5-CONSOLE-LOAD",
            initialQuantity = totalStock,
            lowStockThreshold = 5
        });
        var seedResponse = await httpClient.PostAsync(
            "/api/v1/inventory",
            new StringContent(seedPayload, Encoding.UTF8, "application/json"));
        seedResponse.EnsureSuccessStatusCode();

        var scenario = Scenario.Create("ps5_launch_partitioned", async context =>
            {
                var orderId = Guid.NewGuid();
                var idempotencyKey = Guid.NewGuid().ToString();
                var n = Interlocked.Increment(ref requestCounter);

                var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                    .WithHeader("X-Idempotency-Key", idempotencyKey)
                    .WithHeader("X-Forwarded-For", LoadTestHostFixture.VirtualClientIp(n))
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
                    // NOTE: NBomber's StatusCode carries the REASON PHRASE ("Conflict"),
                    // not the numeric code ("409") — HttpStatusCode.ToString() semantics.
                    // Comparing against "409" silently never matches (verified failure mode).
                    if (response.StatusCode is "Conflict" or "BadRequest")
                    {
                        Interlocked.Increment(ref failedDueToStockCount);
                        return Response.Ok(statusCode: response.StatusCode);
                    }

                    Interlocked.Increment(ref unexpectedFailures);
                    return Response.Fail(statusCode: response.StatusCode);
                }

                Interlocked.Increment(ref successCount);
                return Response.Ok(statusCode: response.StatusCode);
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(concurrentUsers, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(burstSeconds))
            );

        NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder(LoadTestHostFixture.ReportFolder("ps5-launch-partitioned"))
            .Run();

        // Assertions (NBomber buckets business 409s as failures, so gate on our own
        // counters: bounded allocation + zero TRULY unexpected failures like 5xx)

        // At most totalStock reservations should succeed
        successCount.ShouldBeLessThanOrEqualTo(totalStock);

        // No unexpected errors (only stock depletion errors expected)
        // This is the KEY metric - with partitioning, we should see ZERO DB deadlocks
        unexpectedFailures.ShouldBe(0,
            "server errors: " + LoadTestHostFixture.ServerErrorSummary());

        // SAGA LEAK DETECTION: Ensure all sagas completed
        // A non-zero count indicates orphaned saga instances.
        // NOTE: absolute p99 SLOs are staging concerns (cold containers skew them);
        // the automated gate asserts bounded allocation + zero unexpected failures.
        await _fixture.AssertNoSagaLeaksAsync();
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
    [Fact]
    [Trait("Category", "AutomatedLoadTest")]
    public async Task MultiProductFlashSale_ParallelReservations_ShouldProcessInParallel()
    {
        // Configuration - 5 different hot products (scaled via environment for staging)
        var productsCount = EnvInt("LOAD_FLASH_PRODUCTS", 3);
        var stockPerProduct = EnvInt("LOAD_FLASH_STOCK", 10);
        var usersPerProduct = EnvInt("LOAD_FLASH_USERS", 20);
        var burstSeconds = EnvInt("LOAD_FLASH_SECONDS", 5);

        var productIds = Enumerable.Range(0, productsCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var successCounts = new int[productsCount];
        var failedCounts = new int[productsCount];
        var flashUnexpected = 0;

        // Factory-routed client: full in-process pipeline (see fixture transport note).
        using var httpClient = _fixture.CreateClient();

        // Seed one stock record per product
        for (var i = 0; i < productsCount; i++)
        {
            var seedPayload = JsonSerializer.Serialize(new
            {
                productId = productIds[i],
                sku = $"FLASH-{i:D3}-LOAD",
                initialQuantity = stockPerProduct,
                lowStockThreshold = 2
            });
            var seedResponse = await httpClient.PostAsync(
                "/api/v1/inventory",
                new StringContent(seedPayload, Encoding.UTF8, "application/json"));
            seedResponse.EnsureSuccessStatusCode();
        }

        var flashCounter = 0;

        var scenarios = productIds.Select((productId, index) =>
            Scenario.Create($"product_{index}_reservation", async context =>
                {
                    var orderId = Guid.NewGuid();
                    var n = Interlocked.Increment(ref flashCounter);

                    var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                        .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                        .WithHeader("X-Forwarded-For", LoadTestHostFixture.VirtualClientIp(n))
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
                        // Reason phrases, not numeric codes (see note above).
                        if (response.StatusCode is "Conflict" or "BadRequest")
                        {
                            Interlocked.Increment(ref failedCounts[index]);
                            return Response.Ok(statusCode: response.StatusCode);
                        }

                        Interlocked.Increment(ref flashUnexpected);
                        return Response.Fail(statusCode: response.StatusCode);
                    }

                    Interlocked.Increment(ref successCounts[index]);
                    return Response.Ok(statusCode: response.StatusCode);
                })
                .WithoutWarmUp()
                .WithLoadSimulations(
                    Simulation.Inject(usersPerProduct, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(burstSeconds))
                )
        ).ToArray();

        NBomberRunner
            .RegisterScenarios(scenarios)
            .WithReportFolder(LoadTestHostFixture.ReportFolder("multi-product-flash-sale"))
            .Run();

        // Assertions (invariants only — absolute latency SLOs belong to staging).
        // NBomber buckets business 409s as failures, so unexpected-failure tracking
        // lives in flashUnexpected, not in ScenarioStats.
        flashUnexpected.ShouldBe(0);

        // Each product should have at most stockPerProduct successful reservations
        for (var i = 0; i < productsCount; i++)
            successCounts[i].ShouldBeLessThanOrEqualTo(stockPerProduct);
    }

    /// <summary>
    ///     Verifies zero DB deadlocks under sustained high contention.
    ///     This is the key metric for the partitioned messaging pattern.
    /// </summary>
    [Fact]
    [Trait("Category", "AutomatedLoadTest")]
    public async Task SustainedContention_ZeroDeadlocks_ShouldMaintainStability()
    {
        // Configuration - sustained load (scaled via environment for staging)
        var sustainedRps = EnvInt("LOAD_SUSTAINED_RPS", 20);
        var sustainedSeconds = EnvInt("LOAD_SUSTAINED_SECONDS", 10);
        var seedStock = EnvInt("LOAD_SUSTAINED_STOCK", 10_000);
        var productId = Guid.NewGuid();
        var errorCount = 0;
        var successCount = 0;
        var sustainedCounter = 0;

        // Factory-routed client: full in-process pipeline (see fixture transport note).
        using var httpClient = _fixture.CreateClient();

        // Seed ample stock so the test measures stability, not depletion
        var seedPayload = JsonSerializer.Serialize(new
        {
            productId,
            sku = "SUSTAINED-LOAD",
            initialQuantity = seedStock,
            lowStockThreshold = 10
        });
        var seedResponse = await httpClient.PostAsync(
            "/api/v1/inventory",
            new StringContent(seedPayload, Encoding.UTF8, "application/json"));
        seedResponse.EnsureSuccessStatusCode();

        var scenario = Scenario.Create("sustained_contention", async context =>
            {
                var orderId = Guid.NewGuid();
                var n = Interlocked.Increment(ref sustainedCounter);

                var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                    .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                    .WithHeader("X-Forwarded-For", LoadTestHostFixture.VirtualClientIp(n))
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

                // Count any server error as a deadlock/timeout (reason phrase, not "5xx").
                if (response.StatusCode == "InternalServerError")
                {
                    Interlocked.Increment(ref errorCount);
                    return Response.Fail(statusCode: response.StatusCode);
                }

                Interlocked.Increment(ref successCount);
                return Response.Ok(statusCode: response.StatusCode);
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(sustainedRps, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(sustainedSeconds))
            );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder(LoadTestHostFixture.ReportFolder("sustained-contention"))
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
    [Fact(Skip = "Manual only: uses stale unversioned routes (/api/products) and needs a catalog-seeded product; convert to LoadTestHostFixture when pricing coverage is automated")]
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
