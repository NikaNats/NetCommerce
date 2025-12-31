using NBomber.CSharp;
using NBomber.Http.CSharp;
using Shouldly;
using System.Net.Http.Json;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
/// Load tests simulating the PS5 launch scenario.
/// High-demand inventory reservations with limited stock.
/// </summary>
public class PS5LaunchLoadTests
{
    /// <summary>
    /// Simulates PS5 console launch: 1000 users trying to reserve 100 units.
    /// Tests system behavior under extreme contention.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API")]
    public void PS5Launch_HighDemandReservation_ShouldHandleConcurrency()
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

        var scenario = Scenario.Create("ps5_launch_reservation", async context =>
        {
            var orderId = Guid.NewGuid();
            var idempotencyKey = Guid.NewGuid().ToString();

            var request = Http.CreateRequest("POST", "/api/inventory/reserve")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        productId,
                        orderId,
                        quantity = 1
                    }),
                    System.Text.Encoding.UTF8,
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
            Simulation.Inject(rate: concurrentUsers, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(5))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/ps5-launch")
            .Run();

        // Assertions
        var scenarioStats = stats.ScenarioStats[0];
        
        // At most totalStock reservations should succeed
        successCount.ShouldBeLessThanOrEqualTo(totalStock);
        
        // No unexpected errors (only stock depletion errors expected)
        scenarioStats.Fail.Request.Count.ShouldBe(0);
        
        // Response time under load should be reasonable (< 500ms p99)
        scenarioStats.Ok.Latency.Percent99.ShouldBeLessThan(500);
    }

    /// <summary>
    /// Tests optimistic locking under concurrent updates.
    /// Multiple users trying to update the same product price.
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
                    System.Text.Json.JsonSerializer.Serialize(new { amount = newPrice, currency = "GEL" }),
                    System.Text.Encoding.UTF8,
                    "application/json"));

            var response = await Http.Send(httpClient, request);

            if (response.StatusCode == "409") // Conflict - optimistic lock failure
            {
                Interlocked.Increment(ref conflictCount);
                return Response.Ok(statusCode: "409"); // Expected behavior
            }

            if (!response.IsError)
            {
                Interlocked.Increment(ref successCount);
            }

            return response.IsError
                ? Response.Fail(statusCode: response.StatusCode)
                : Response.Ok(statusCode: response.StatusCode);
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(5))
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
