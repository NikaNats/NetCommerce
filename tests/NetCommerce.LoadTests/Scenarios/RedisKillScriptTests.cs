#nullable enable

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.LoadTests.Assertions;
using Shouldly;
using Testcontainers.Redis;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
///     Phase 7: Infrastructure Health-Check Drill - Redis Kill Script.
///
///     <para>
///     <b>Requirement:</b> Create a "Kill Script" that stops the Redis container while
///     a load test is running. Verify that the Inventory module returns 503 Service Unavailable
///     instead of allowing un-locked reservations.
///     </para>
///
///     <para>
///     <b>Critical Validation:</b>
///     - When Redis dies mid-load, API must NOT allow reservations to proceed without locks
///     - System must fail-closed (503) rather than fail-open (allow overselling)
///     - Health check must report unhealthy within SLA (configurable, default: 5s)
///     </para>
/// </summary>
public class RedisKillScriptTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private HttpClient? _httpClient;

    // Configuration
    private const string ApiBaseUrl = "http://localhost:5000";
    private const int LoadTestDurationSeconds = 30;
    private const int RequestsPerSecond = 50;
    private const int KillRedisAfterSeconds = 10; // Kill Redis after 10s of load

    // xUnit v3: ValueTask InitializeAsync
    public async ValueTask InitializeAsync()
    {
        // Start a managed Redis container for this test
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7.4")
            .WithPortBinding(6380, 6379) // Use different port to avoid conflicts
            .Build();

        await _redisContainer.StartAsync();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    // xUnit v3: ValueTask DisposeAsync
    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();

        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }
    }

    /// <summary>
    ///     Runs a load test while killing Redis mid-flight.
    ///     Validates that the system responds with 503 after Redis dies.
    ///
    ///     <para>
    ///     Timeline:
    ///     - T+0s: Load test starts (50 RPS)
    ///     - T+10s: Redis container is killed
    ///     - T+10-30s: Verify API returns 503 (not 200 with un-locked reservation)
    ///     </para>
    /// </summary>
    [Fact]
    [Trait("Category", "LoadTest")]
    [Trait("Category", "RequiresApi")]
    public async Task KillRedis_DuringLoadTest_ShouldReturn503NotAllowOverselling()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // METRICS COLLECTORS
        // ═══════════════════════════════════════════════════════════════════════════
        var successBeforeKill = 0;
        var successAfterKill = 0;
        var service503AfterKill = 0;
        var otherErrorsAfterKill = 0;
        var redisKilledAt = DateTime.MinValue;
        var killSignalSent = false;

        // Product for testing
        var hotProductId = Guid.NewGuid();

        // ═══════════════════════════════════════════════════════════════════════════
        // NBOMBER SCENARIO: Continuous reservation requests
        // ═══════════════════════════════════════════════════════════════════════════
        var loadScenario = Scenario.Create("redis_kill_drill", async context =>
        {
            var orderId = Guid.NewGuid();
            var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        orderId,
                        items = new[]
                        {
                            new { productId = hotProductId, quantity = 1, sku = "SKU-REDIS-KILL-001" }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            try
            {
                var response = await Http.Send(_httpClient!, request);

                var statusCode = response.StatusCode ?? "";
                var isAfterKill = redisKilledAt != DateTime.MinValue;

                if (!isAfterKill)
                {
                    if (statusCode.StartsWith("2"))
                        Interlocked.Increment(ref successBeforeKill);
                }
                else
                {
                    if (statusCode == "503")
                    {
                        Interlocked.Increment(ref service503AfterKill);
                    }
                    else if (statusCode.StartsWith("2"))
                    {
                        Interlocked.Increment(ref successAfterKill);
                    }
                    else
                    {
                        Interlocked.Increment(ref otherErrorsAfterKill);
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                context.Logger.Warning(ex, "Request failed");
                return Response.Fail<HttpResponseMessage>(message: ex.Message);
            }
        })
        .WithLoadSimulations(
            Simulation.Inject(
                rate: RequestsPerSecond,
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromSeconds(LoadTestDurationSeconds)))
        .WithoutWarmUp();

        // ═══════════════════════════════════════════════════════════════════════════
        // KILL SCRIPT: Background task to kill Redis after delay
        // ═══════════════════════════════════════════════════════════════════════════
        var killTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(KillRedisAfterSeconds));

            Console.WriteLine($"[KILL SCRIPT] Stopping Redis container at {DateTime.UtcNow:O}...");

            await _redisContainer!.StopAsync();
            redisKilledAt = DateTime.UtcNow;
            killSignalSent = true;

            Console.WriteLine($"[KILL SCRIPT] Redis container stopped successfully.");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // EXECUTE LOAD TEST
        // ═══════════════════════════════════════════════════════════════════════════
        var stats = NBomberRunner
            .RegisterScenarios(loadScenario)
            .WithReportFolder("reports/redis-kill-drill")
            .Run();

        // Wait for kill task to complete
        await killTask;

        // ═══════════════════════════════════════════════════════════════════════════
        // ASSERTIONS: Validate fail-closed behavior
        // ═══════════════════════════════════════════════════════════════════════════
        Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           REDIS KILL DRILL - RESULTS SUMMARY              ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Redis killed at:           {redisKilledAt:HH:mm:ss.fff}                 ║");
        Console.WriteLine($"║ Success BEFORE kill:       {successBeforeKill,6} requests              ║");
        Console.WriteLine($"║ Success AFTER kill:        {successAfterKill,6} requests (DANGER!)     ║");
        Console.WriteLine($"║ 503 AFTER kill:            {service503AfterKill,6} requests (EXPECTED)   ║");
        Console.WriteLine($"║ Other errors AFTER kill:   {otherErrorsAfterKill,6} requests              ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        // CRITICAL ASSERTION: After Redis dies, no reservations should succeed
        // If successAfterKill > 0, the system allowed un-locked reservations = OVERSELLING RISK
        successAfterKill.ShouldBe(0,
            $"CRITICAL FAILURE: {successAfterKill} reservations succeeded WITHOUT Redis locks! " +
            "This indicates the system is vulnerable to overselling when Redis is unavailable.");

        // System should be returning 503 Service Unavailable
        service503AfterKill.ShouldBeGreaterThan(0,
            "Expected 503 responses after Redis was killed, but received none. " +
            "Health check may not be detecting Redis failure.");
    }

    /// <summary>
    ///     Validates that the health endpoint reports unhealthy within SLA
    ///     when Redis is killed.
    /// </summary>
    [Fact]
    [Trait("Category", "LoadTest")]
    [Trait("Category", "RequiresApi")]
    public async Task KillRedis_HealthEndpoint_ShouldReportUnhealthyWithinSla()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════════════════
        const int maxSlaSeconds = 5; // Health check must detect failure within 5s
        var stopwatch = new Stopwatch();

        // ═══════════════════════════════════════════════════════════════════════════
        // VERIFY HEALTHY BEFORE KILL
        // ═══════════════════════════════════════════════════════════════════════════
        var healthBefore = await _httpClient!.GetAsync("/health/ready");
        healthBefore.StatusCode.ShouldBe(HttpStatusCode.OK,
            "Health check should be healthy before Redis kill");

        Console.WriteLine($"[HEALTH DRILL] Initial health check: {healthBefore.StatusCode}");

        // ═══════════════════════════════════════════════════════════════════════════
        // KILL REDIS
        // ═══════════════════════════════════════════════════════════════════════════
        Console.WriteLine($"[HEALTH DRILL] Killing Redis at {DateTime.UtcNow:O}...");
        await _redisContainer!.StopAsync();
        stopwatch.Start();

        // ═══════════════════════════════════════════════════════════════════════════
        // POLL HEALTH ENDPOINT UNTIL UNHEALTHY
        // ═══════════════════════════════════════════════════════════════════════════
        var detectedUnhealthy = false;
        while (stopwatch.Elapsed.TotalSeconds < maxSlaSeconds + 2) // +2s buffer
        {
            var healthAfter = await _httpClient.GetAsync("/health/ready");
            Console.WriteLine(
                $"[HEALTH DRILL] T+{stopwatch.Elapsed.TotalSeconds:F1}s: {healthAfter.StatusCode}");

            if (healthAfter.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                detectedUnhealthy = true;
                stopwatch.Stop();
                break;
            }

            await Task.Delay(500); // Poll every 500ms
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // ASSERTIONS
        // ═══════════════════════════════════════════════════════════════════════════
        Console.WriteLine($"\n[HEALTH DRILL] Detection time: {stopwatch.Elapsed.TotalSeconds:F2}s");

        detectedUnhealthy.ShouldBeTrue(
            $"Health check did not report unhealthy within {maxSlaSeconds}s SLA");

        stopwatch.Elapsed.TotalSeconds.ShouldBeLessThanOrEqualTo(maxSlaSeconds,
            $"Health check detected Redis failure in {stopwatch.Elapsed.TotalSeconds:F2}s, " +
            $"which exceeds the {maxSlaSeconds}s SLA");

        Console.WriteLine($"[HEALTH DRILL] ✓ Health check detected Redis failure within SLA");
    }

    /// <summary>
    ///     Simulates a "kill and recover" scenario to ensure the system
    ///     can resume normal operation after Redis comes back.
    /// </summary>
    [Fact]
    [Trait("Category", "LoadTest")]
    [Trait("Category", "RequiresApi")]
    public async Task KillAndRecoverRedis_ShouldResumeNormalOperation()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // PHASE 1: Normal operation
        // ═══════════════════════════════════════════════════════════════════════════
        var phase1Success = await TryReserveAsync();
        Console.WriteLine($"[RECOVERY] Phase 1 (before kill): {(phase1Success ? "SUCCESS" : "FAILED")}");

        // ═══════════════════════════════════════════════════════════════════════════
        // PHASE 2: Kill Redis
        // ═══════════════════════════════════════════════════════════════════════════
        Console.WriteLine("[RECOVERY] Killing Redis...");
        await _redisContainer!.StopAsync();
        await Task.Delay(2000); // Wait for detection

        var phase2Status = await GetReservationStatusAsync();
        Console.WriteLine($"[RECOVERY] Phase 2 (after kill): {phase2Status}");
        phase2Status.ShouldBeOneOf(503, 500); // Should be unavailable or error

        // ═══════════════════════════════════════════════════════════════════════════
        // PHASE 3: Restart Redis
        // ═══════════════════════════════════════════════════════════════════════════
        Console.WriteLine("[RECOVERY] Restarting Redis...");
        await _redisContainer.StartAsync();
        await Task.Delay(5000); // Wait for health check to detect recovery

        var phase3Success = await TryReserveAsync();
        Console.WriteLine($"[RECOVERY] Phase 3 (after recovery): {(phase3Success ? "SUCCESS" : "FAILED")}");

        // After Redis recovers, reservations should work again
        phase3Success.ShouldBeTrue(
            "System should resume normal operation after Redis recovers");
    }

    private async Task<bool> TryReserveAsync()
    {
        try
        {
            var response = await _httpClient!.PostAsync(
                "/api/v1/inventory/reserve",
                new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        orderId = Guid.NewGuid(),
                        items = new[]
                        {
                            new { productId = Guid.NewGuid(), quantity = 1, sku = "SKU-TEST-001" }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> GetReservationStatusAsync()
    {
        try
        {
            var response = await _httpClient!.PostAsync(
                "/api/v1/inventory/reserve",
                new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        orderId = Guid.NewGuid(),
                        items = new[]
                        {
                            new { productId = Guid.NewGuid(), quantity = 1, sku = "SKU-TEST-001" }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            return (int)response.StatusCode;
        }
        catch
        {
            return 0;
        }
    }
}
