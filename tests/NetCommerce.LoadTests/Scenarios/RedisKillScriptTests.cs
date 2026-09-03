#nullable enable

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NetCommerce.LoadTests.Assertions;
using Shouldly;
using Testcontainers.Redis;
using Xunit;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
///     Phase 7: Infrastructure Health-Check Drill - Redis Kill Script.
///
///     <para>
///     <b>Requirement:</b> Stops the Redis container while a load test is running.
///     Verifies that the Inventory module returns 503 Service Unavailable
///     instead of allowing un-locked reservations.
///     </para>
///
///     <para>
///     <b>Execution:</b> Run against a live running API instance:
///     <code>dotnet test --filter "FullyQualifiedName~RedisKillScriptTests"</code>
///     </para>
/// </summary>
public class RedisKillScriptTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private HttpClient? _httpClient;

    private static readonly string ApiBaseUrl = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5000";
    private const int LoadTestDurationSeconds = 30;
    private const int RequestsPerSecond = 50;
    private const int KillRedisAfterSeconds = 10;

    public async ValueTask InitializeAsync()
    {
        // Bind to standard Redis port 6379 so the running API's default connection string is targeted
        _redisContainer = new RedisBuilder()
            .WithImage("redis:8-alpine")
            .WithPortBinding(6379, 6379)
            .Build();

        await _redisContainer.StartAsync();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();

        if (_redisContainer is not null)
        {
            await _redisContainer.DisposeAsync();
        }
    }

    /// <summary>
    ///     Runs a load test while killing Redis mid-flight.
    ///     Validates that the system responds with 503 after Redis dies.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API (e.g. dotnet run --project src/Api)")]
    [Trait("Category", "LoadTest")]
    [Trait("Category", "RequiresApi")]
    public async Task KillRedis_DuringLoadTest_ShouldReturn503NotAllowOverselling()
    {
        var successBeforeKill = 0;
        var successAfterKill = 0;
        var service503AfterKill = 0;
        var otherErrorsAfterKill = 0;
        var redisKilledAt = DateTime.MinValue;

        var hotProductId = Guid.NewGuid();

        var loadScenario = Scenario.Create("redis_kill_drill", async context =>
        {
            var orderId = Guid.NewGuid();

            // Correct flat payload matching ReserveStockCommand
            var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        productId = hotProductId,
                        orderId,
                        quantity = 1
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

        var killTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(KillRedisAfterSeconds));

            Console.WriteLine($"[KILL SCRIPT] Stopping Redis container at {DateTime.UtcNow:O}...");
            await _redisContainer!.StopAsync();
            redisKilledAt = DateTime.UtcNow;
            Console.WriteLine($"[KILL SCRIPT] Redis container stopped successfully.");
        });

        var stats = NBomberRunner
            .RegisterScenarios(loadScenario)
            .WithReportFolder("reports/redis-kill-drill")
            .Run();

        await killTask;

        Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           REDIS KILL DRILL - RESULTS SUMMARY              ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Redis killed at:           {redisKilledAt:HH:mm:ss.fff}                 ║");
        Console.WriteLine($"║ Success BEFORE kill:       {successBeforeKill,6} requests              ║");
        Console.WriteLine($"║ Success AFTER kill:        {successAfterKill,6} requests (DANGER!)     ║");
        Console.WriteLine($"║ 503 AFTER kill:            {service503AfterKill,6} requests (EXPECTED)   ║");
        Console.WriteLine($"║ Other errors AFTER kill:   {otherErrorsAfterKill,6} requests              ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

        successAfterKill.ShouldBe(0,
            $"CRITICAL FAILURE: {successAfterKill} reservations succeeded WITHOUT Redis locks!");

        service503AfterKill.ShouldBeGreaterThan(0,
            "Expected 503 responses after Redis was killed, but received none.");
    }

    /// <summary>
    ///     Validates that the health endpoint reports unhealthy within SLA when Redis is killed.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API (e.g. dotnet run --project src/Api)")]
    [Trait("Category", "LoadTest")]
    [Trait("Category", "RequiresApi")]
    public async Task KillRedis_HealthEndpoint_ShouldReportUnhealthyWithinSla()
    {
        const int maxSlaSeconds = 5;
        var stopwatch = new Stopwatch();

        var healthBefore = await _httpClient!.GetAsync("/health/ready");
        healthBefore.StatusCode.ShouldBe(HttpStatusCode.OK,
            "Health check should be healthy before Redis kill");

        Console.WriteLine($"[HEALTH DRILL] Initial health check: {healthBefore.StatusCode}");

        Console.WriteLine($"[HEALTH DRILL] Killing Redis at {DateTime.UtcNow:O}...");
        await _redisContainer!.StopAsync();
        stopwatch.Start();

        var detectedUnhealthy = false;
        while (stopwatch.Elapsed.TotalSeconds < maxSlaSeconds + 2)
        {
            var healthAfter = await _httpClient.GetAsync("/health/ready");
            Console.WriteLine($"[HEALTH DRILL] T+{stopwatch.Elapsed.TotalSeconds:F1}s: {healthAfter.StatusCode}");

            if (healthAfter.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                detectedUnhealthy = true;
                stopwatch.Stop();
                break;
            }

            await Task.Delay(500);
        }

        Console.WriteLine($"\n[HEALTH DRILL] Detection time: {stopwatch.Elapsed.TotalSeconds:F2}s");

        detectedUnhealthy.ShouldBeTrue(
            $"Health check did not report unhealthy within {maxSlaSeconds}s SLA");

        stopwatch.Elapsed.TotalSeconds.ShouldBeLessThanOrEqualTo(maxSlaSeconds,
            $"Health check detected Redis failure in {stopwatch.Elapsed.TotalSeconds:F2}s, which exceeds the {maxSlaSeconds}s SLA");
    }

    /// <summary>
    ///     Simulates a "kill and recover" scenario to ensure the system
    ///     resumes normal operation after Redis comes back online.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API (e.g. dotnet run --project src/Api)")]
    [Trait("Category", "LoadTest")]
    [Trait("Category", "RequiresApi")]
    public async Task KillAndRecoverRedis_ShouldResumeNormalOperation()
    {
        var phase1Success = await TryReserveAsync();
        Console.WriteLine($"[RECOVERY] Phase 1 (before kill): {(phase1Success ? "SUCCESS" : "FAILED")}");

        Console.WriteLine("[RECOVERY] Killing Redis...");
        await _redisContainer!.StopAsync();
        await Task.Delay(2000);

        var phase2Status = await GetReservationStatusAsync();
        Console.WriteLine($"[RECOVERY] Phase 2 (after kill): {phase2Status}");
        phase2Status.ShouldBeOneOf(503, 500);

        Console.WriteLine("[RECOVERY] Restarting Redis...");
        await _redisContainer.StartAsync();
        await Task.Delay(5000);

        var phase3Success = await TryReserveAsync();
        Console.WriteLine($"[RECOVERY] Phase 3 (after recovery): {(phase3Success ? "SUCCESS" : "FAILED")}");

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
                        productId = Guid.NewGuid(),
                        orderId = Guid.NewGuid(),
                        quantity = 1
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
                        productId = Guid.NewGuid(),
                        orderId = Guid.NewGuid(),
                        quantity = 1
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
