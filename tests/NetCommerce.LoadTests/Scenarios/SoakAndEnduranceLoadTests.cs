#nullable enable

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NetCommerce.LoadTests.Fixtures;
using Shouldly;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
///     2026 Production-Readiness Soak &amp; Endurance Testing Suite.
///     Applies steady-state traffic (200-500 RPS) over extended durations (1h - 48h)
///     to detect memory leaks, Npgsql connection pool exhaustion, and Redis degradation.
/// </summary>
[Collection(nameof(LoadTestCollection))]
public sealed class SoakAndEnduranceLoadTests
{
    private readonly LoadTestHostFixture _fixture;

    public SoakAndEnduranceLoadTests(LoadTestHostFixture fixture)
    {
        _fixture = fixture;
    }

    private static int EnvInt(string name, int @default) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : @default;

    /// <summary>
    ///     Automated endurance smoke: catalog reads at steady RPS against the
    ///     self-bootstrapped host. Fast CI profile (10s/50rps) by default; scales via
    ///     SOAK_DURATION_SECONDS / SOAK_TARGET_RPS for scheduled multi-hour runs.
    ///     The full 24h mixed-profile suite below stays manual for staging.
    /// </summary>
    [Fact]
    [Trait("Category", "AutomatedLoadTest")]
    public void ContinuousLoad_Smoke_ShouldMaintainResourceEquilibrium()
    {
        var soakSeconds = EnvInt("SOAK_DURATION_SECONDS", 10);
        var targetRps = EnvInt("SOAK_TARGET_RPS", 50);

        // Factory-routed client: full in-process pipeline (see fixture transport note).
        using var httpClient = _fixture.CreateClient();

        var poolErrors = 0;
        var totalRequests = 0L;
        var requestCounter = 0;
        // Shard rate-limit partitions like distinct prod users (see fixture header doc).
        var userPool = Math.Max(targetRps * 5, 50);

        var catalogScenario = Scenario.Create("automated_catalog_soak", async context =>
        {
            var page = Random.Shared.Next(1, 5);
            var n = Interlocked.Increment(ref requestCounter);
            var request = Http.CreateRequest("GET", $"/api/v1/products?page={page}&pageSize=20")
                .WithHeader(LoadTestHostFixture.LoadTestUserHeader, $"soak-user-{n % userPool}")
                .WithHeader("X-Forwarded-For", LoadTestHostFixture.VirtualClientIp(n));

            var response = await Http.Send(httpClient, request);
            Interlocked.Increment(ref totalRequests);

            if (response.IsError)
            {
                if (response.Message?.Contains("connection pool", StringComparison.OrdinalIgnoreCase) == true)
                    Interlocked.Increment(ref poolErrors);

                return Response.Fail(statusCode: response.StatusCode);
            }

            return Response.Ok(statusCode: response.StatusCode);
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(
                rate: targetRps,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(soakSeconds))
        );

        var stats = NBomberRunner
            .RegisterScenarios(catalogScenario)
            .WithReportFolder(LoadTestHostFixture.ReportFolder("soak-smoke"))
            .Run();

        poolErrors.ShouldBe(0, $"Npgsql connection pool leak during {soakSeconds}s endurance smoke!");
        stats.ScenarioStats[0].Fail.Request.Count.ShouldBe(0, "Failures during catalog soak smoke.");
        totalRequests.ShouldBeGreaterThan(0, "Smoke generated no requests.");
    }

    private static readonly string ApiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5000";
    private static readonly int SoakDurationHours = int.TryParse(Environment.GetEnvironmentVariable("SOAK_DURATION_HOURS"), out var hours) ? hours : 24;
    private static readonly int TargetRps = int.TryParse(Environment.GetEnvironmentVariable("SOAK_TARGET_RPS"), out var rps) ? rps : 250;

    [Fact(Skip = "Run in Staging/Performance environment: requires running API and infrastructure")]
    [Trait("Category", "SoakTest")]
    [Trait("Category", "LongRunning")]
    public async Task ContinuousLoad_24To48Hours_ShouldMaintainResourceEquilibrium()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };

        var memorySnapshots = new List<MemoryMetricSnapshot>();
        var poolErrors = 0;
        var redisTimeouts = 0;
        var totalRequests = 0L;

        var sampleInterval = TimeSpan.FromMinutes(10);
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromHours(SoakDurationHours));

        // Background monitor: collects diagnostic metrics every 10 minutes
        var metricsMonitorTask = Task.Run(async () =>
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var response = await httpClient.GetAsync("/health/ready", cancellationTokenSource.Token);
                    var processInfo = Process.GetCurrentProcess();

                    var snapshot = new MemoryMetricSnapshot(
                        Timestamp: DateTime.UtcNow,
                        WorkingSetMb: processInfo.WorkingSet64 / (1024 * 1024),
                        PrivateMemoryMb: processInfo.PrivateMemorySize64 / (1024 * 1024),
                        IsHealthy: response.IsSuccessStatusCode
                    );

                    lock (memorySnapshots)
                    {
                        memorySnapshots.Add(snapshot);
                    }

                    Console.WriteLine($"[SOAK METRICS] {snapshot.Timestamp:O} | WorkingSet: {snapshot.WorkingSetMb} MB | PrivateMemory: {snapshot.PrivateMemoryMb} MB | Health: {response.StatusCode}");
                }
                catch (Exception ex) when (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Console.WriteLine($"[SOAK MONITOR ERROR] Failed to sample health/metrics: {ex.Message}");
                }

                try
                {
                    await Task.Delay(sampleInterval, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        // ═══════════════════════════════════════════════════════════════════════
        // NBOMBER SCENARIOS: Mixed Production Profile
        // 70% Catalog Reads (Cached & Uncached), 20% Basket, 10% Checkout
        // ═══════════════════════════════════════════════════════════════════════

        var catalogReadScenario = Scenario.Create("soak_catalog_read", async context =>
        {
            var page = Random.Shared.Next(1, 20);
            var request = Http.CreateRequest("GET", $"/api/v1/products?page={page}&pageSize=20");

            var response = await Http.Send(httpClient, request);
            Interlocked.Increment(ref totalRequests);

            if (response.IsError)
            {
                if (response.Message?.Contains("connection pool", StringComparison.OrdinalIgnoreCase) == true)
                    Interlocked.Increment(ref poolErrors);

                if (response.Message?.Contains("redis", StringComparison.OrdinalIgnoreCase) == true ||
                    response.Message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true)
                    Interlocked.Increment(ref redisTimeouts);

                return Response.Fail(statusCode: response.StatusCode);
            }

            return Response.Ok(statusCode: response.StatusCode);
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(
                rate: (int)(TargetRps * 0.70),
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromHours(SoakDurationHours))
        );

        var basketScenario = Scenario.Create("soak_basket_mutations", async context =>
        {
            var productId = Guid.NewGuid();

            var request = Http.CreateRequest("POST", "/api/v1/basket/items")
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        productId,
                        productName = "Soak Test Item",
                        sku = "SKU-SOAK-01",
                        quantity = 1,
                        unitPrice = 49.99,
                        imageUrl = "https://cdn.netcommerce.com/soak.jpg"
                    }),
                    Encoding.UTF8,
                    "application/json"));

            var response = await Http.Send(httpClient, request);
            Interlocked.Increment(ref totalRequests);

            if (response.IsError)
            {
                if (response.Message?.Contains("redis", StringComparison.OrdinalIgnoreCase) == true)
                    Interlocked.Increment(ref redisTimeouts);

                return Response.Fail(statusCode: response.StatusCode);
            }

            return Response.Ok(statusCode: response.StatusCode);
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(
                rate: (int)(TargetRps * 0.20),
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromHours(SoakDurationHours))
        );

        var checkoutScenario = Scenario.Create("soak_order_creations", async context =>
        {
            var idempotencyKey = Guid.NewGuid();
            var orderPayload = new
            {
                customerId = Guid.NewGuid(),
                customerEmail = "soak.agent@netcommerce.internal",
                customerName = "Soak Agent",
                items = new[]
                {
                    new
                    {
                        productId = Guid.NewGuid(),
                        quantity = 1,
                        expectedPrice = 99.99
                    }
                },
                shippingAddress = new
                {
                    street = "123 Soak Way",
                    city = "Tbilisi",
                    state = "GA",
                    postalCode = "0108",
                    country = "GE",
                    recipientName = "Soak Agent",
                    phoneNumber = "+995555000111"
                },
                billingAddress = new
                {
                    street = "123 Soak Way",
                    city = "Tbilisi",
                    state = "GA",
                    postalCode = "0108",
                    country = "GE"
                },
                paymentMethod = "CreditCard",
                idempotencyKey = idempotencyKey.ToString()
            };

            var request = Http.CreateRequest("POST", "/api/v1/orders")
                .WithHeader("X-Idempotency-Key", idempotencyKey.ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(JsonSerializer.Serialize(orderPayload), Encoding.UTF8, "application/json"));

            var response = await Http.Send(httpClient, request);
            Interlocked.Increment(ref totalRequests);

            if (response.IsError && response.StatusCode != "401" && response.StatusCode != "422")
            {
                if (response.Message?.Contains("connection pool", StringComparison.OrdinalIgnoreCase) == true)
                    Interlocked.Increment(ref poolErrors);

                return Response.Fail(statusCode: response.StatusCode);
            }

            return Response.Ok(statusCode: response.StatusCode);
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(
                rate: (int)(TargetRps * 0.10),
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromHours(SoakDurationHours))
        );

        // ═══════════════════════════════════════════════════════════════════════
        // EXECUTE AND VERIFY
        // ═══════════════════════════════════════════════════════════════════════

        var stats = NBomberRunner
            .RegisterScenarios(catalogReadScenario, basketScenario, checkoutScenario)
            .WithReportFolder($"./load-test-reports/soak-{SoakDurationHours}h")
            .Run();

        cancellationTokenSource.Cancel();
        await Task.WhenAny(metricsMonitorTask, Task.Delay(TimeSpan.FromSeconds(5)));

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERTIONS: Long-Term Reliability Invariants
        // ═══════════════════════════════════════════════════════════════════════

        poolErrors.ShouldBe(0,
            $"Npgsql Connection Pool Leak Detected: {poolErrors} requests failed with pool exhaustion during the {SoakDurationHours}h soak test.");

        redisTimeouts.ShouldBe(0,
            $"Redis Multiplexer Degradation: {redisTimeouts} Redis command timeouts encountered.");

        // Check for steady memory leak (WorkingSet should not grow monotonically past warm-up window)
        lock (memorySnapshots)
        {
            if (memorySnapshots.Count > 6)
            {
                var postWarmupSnapshots = memorySnapshots.Skip(3).ToList(); // Ignore first 30 mins
                var initialWorkingSet = postWarmupSnapshots.First().WorkingSetMb;
                var finalWorkingSet = postWarmupSnapshots.Last().WorkingSetMb;
                var growthRatio = (double)finalWorkingSet / Math.Max(initialWorkingSet, 1);

                growthRatio.ShouldBeLessThan(1.5,
                    $"Native AOT Memory Leak: WorkingSet grew from {initialWorkingSet}MB to {finalWorkingSet}MB ({growthRatio:F2}x growth) over {SoakDurationHours} hours.");
            }
        }
    }

    private record MemoryMetricSnapshot(
        DateTime Timestamp,
        long WorkingSetMb,
        long PrivateMemoryMb,
        bool IsHealthy);
}
