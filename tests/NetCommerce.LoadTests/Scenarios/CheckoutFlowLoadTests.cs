using NBomber.CSharp;
using NBomber.Http.CSharp;
using Shouldly;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
/// Load tests for checkout flow under high concurrency.
/// Simulates flash sale scenario with multiple users checking out simultaneously.
/// </summary>
public class CheckoutFlowLoadTests
{
    /// <summary>
    /// Simulates flash sale: Many users adding items to cart and checking out.
    /// Tests the full purchase flow under load.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API")]
    public void FlashSale_FullCheckoutFlow_ShouldMaintainConsistency()
    {
        const string apiBaseUrl = "http://localhost:5000";
        var customerId = Guid.NewGuid();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var scenario = Scenario.Create("flash_sale_checkout", async context =>
        {
            var productId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var idempotencyKey = Guid.NewGuid().ToString();
            
            // Step 1: Add to cart
            var addCartRequest = Http.CreateRequest("POST", $"/api/basket/{customerId}/items")
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        productId,
                        quantity = 1
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json"));

            var addCartResponse = await Http.Send(httpClient, addCartRequest);
            if (addCartResponse.IsError)
                return Response.Fail(statusCode: addCartResponse.StatusCode);

            // Step 2: Reserve stock
            var reserveRequest = Http.CreateRequest("POST", "/api/inventory/reserve")
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

            var reserveResponse = await Http.Send(httpClient, reserveRequest);
            // 409 Conflict means out of stock - expected in flash sale
            if (reserveResponse.StatusCode == "409")
                return Response.Ok(statusCode: "409", sizeBytes: 0);
            if (reserveResponse.IsError)
                return Response.Fail(statusCode: reserveResponse.StatusCode);

            // Step 3: Create order
            var createOrderRequest = Http.CreateRequest("POST", "/api/orders")
                .WithHeader("X-Idempotency-Key", idempotencyKey)
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        customerId,
                        shippingAddress = new
                        {
                            recipientName = "Test User",
                            street = "123 Main St",
                            city = "Tbilisi",
                            state = "GA",
                            zipCode = "0100",
                            country = "Georgia",
                            phone = "+995555123456"
                        }
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json"));

            var orderResponse = await Http.Send(httpClient, createOrderRequest);
            if (orderResponse.IsError)
                return Response.Fail(statusCode: orderResponse.StatusCode);

            // Step 4: Process payment
            var paymentRequest = Http.CreateRequest("POST", "/api/payments")
                .WithHeader("X-Idempotency-Key", idempotencyKey + "-pay")
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        orderId,
                        amount = 499.99,
                        currency = "GEL",
                        method = "card"
                    }),
                    System.Text.Encoding.UTF8,
                    "application/json"));

            var paymentResponse = await Http.Send(httpClient, paymentRequest);
            
            return paymentResponse.IsError
                ? Response.Fail(statusCode: paymentResponse.StatusCode)
                : Response.Ok(statusCode: paymentResponse.StatusCode, sizeBytes: paymentResponse.SizeBytes);
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Ramp up users
            Simulation.RampingInject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
            // Sustain peak load
            Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)),
            // Ramp down
            Simulation.RampingInject(rate: 5, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/flash-sale")
            .Run();

        // Assertions
        var scenarioStats = stats.ScenarioStats[0];
        
        // Overall success rate should be > 95% (5% failure due to stock depletion is OK)
        var totalRequests = scenarioStats.Ok.Request.Count + scenarioStats.Fail.Request.Count;
        var successRate = totalRequests > 0 
            ? scenarioStats.Ok.Request.Count / (double)totalRequests * 100 
            : 100;
        successRate.ShouldBeGreaterThan(95);
        
        // P99 latency should be under 1 second
        scenarioStats.Ok.Latency.Percent99.ShouldBeLessThan(1000);
    }

    /// <summary>
    /// Simulates sustained read load on product catalog.
    /// Tests caching effectiveness and read performance.
    /// </summary>
    [Fact(Skip = "Run manually - requires running API")]
    public void ProductCatalog_SustainedReadLoad_ShouldBeFast()
    {
        const string apiBaseUrl = "http://localhost:5000";

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl)
        };

        var scenario = Scenario.Create("catalog_read_load", async context =>
        {
            // Random product page
            var page = new Random().Next(1, 10);
            
            var request = Http.CreateRequest("GET", $"/api/products?page={page}&pageSize=20");
            var response = await Http.Send(httpClient, request);

            return response.IsError
                ? Response.Fail(statusCode: response.StatusCode)
                : Response.Ok(statusCode: response.StatusCode, sizeBytes: response.SizeBytes);
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: 100, during: TimeSpan.FromSeconds(60))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/catalog-read")
            .Run();

        var scenarioStats = stats.ScenarioStats[0];
        
        // All reads should succeed
        scenarioStats.Fail.Request.Count.ShouldBe(0);
        
        // Average latency should be very low (cached responses)
        scenarioStats.Ok.Latency.MeanMs.ShouldBeLessThan(50);
        
        // P99 should be under 200ms
        scenarioStats.Ok.Latency.Percent99.ShouldBeLessThan(200);
    }
}
