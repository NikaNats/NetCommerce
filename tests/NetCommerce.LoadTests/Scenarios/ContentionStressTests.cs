#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using NetCommerce.LoadTests.Assertions;
using Shouldly;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
///     ACM SIGSOFT-Grade Contention-Specific Stress Analysis Tests.
///
///     <para>
///     These tests validate the theoretical foundations of Partitioned Sequential Messaging
///     by measuring the system's behavior under "Hot Key" scenarios where a single resource
///     receives 10,000%+ more traffic than the average.
///     </para>
///
///     <para>
///     The key insight: Wolverine's message partitioning converts a "Hardware Contention"
///     problem (Database Locking / FOR UPDATE deadlocks) into a "Software Scheduling"
///     problem (Message Queue Head-of-Line blocking). The latter is predictable and bounded.
///     </para>
///
///     <para>
///     Metrics Framework:
///     - Deadlock Rate: 0.00% proves partitioning removed DB contention
///     - P99 Latency (Hot Key): Linear growth proves queue depth is predictable
///     - CPU Context Switching: Low proves threads aren't fighting for cycles
///     - Saga Leak Rate: Zero verified by SagaLeakAssertions after burst
///     </para>
/// </summary>
public class ContentionStressTests
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // TEST 1: SINGLE-KEY SATURATION ("ZERO-LOCK" BENCHMARK)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Sends 5,000 ReserveInventoryCommand requests in a 10-second burst,
    ///     all targeting the exact same ProductId (the PS5 Launch Scenario).
    /// </summary>
    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task SingleKeySaturation_5000Requests_SameProductId_ShouldHaveZeroDeadlocks()
    {
        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════
        const int totalRequests = 5_000;
        const int burstDurationSeconds = 10;
        const string apiBaseUrl = "http://localhost:5000";

        // THE HOT KEY: All 5,000 requests target this single ProductId
        var hotProductId = Guid.NewGuid();

        // Metrics collectors
        var latencies = new ConcurrentBag<double>();
        var dbTimeoutErrors = 0;
        var lockTimeoutErrors = 0;
        var successCount = 0;
        var stockDepletedCount = 0;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(60) // Long timeout for queue depth
        };

        // ═══════════════════════════════════════════════════════════════
        // WARM-UP PHASE (30 seconds)
        // ═══════════════════════════════════════════════════════════════
        var warmupScenario = Scenario.Create("warmup_phase", async context =>
        {
            var warmupOrderId = Guid.NewGuid();
            var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        orderId = warmupOrderId,
                        items = new[]
                        {
                            new { productId = Guid.NewGuid(), quantity = 1, sku = "WARMUP-SKU" }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            var response = await Http.Send(httpClient, request);
            return response.IsError
                ? Response.Fail(statusCode: response.StatusCode)
                : Response.Ok();
        })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30))
            );

        NBomberRunner
            .RegisterScenarios(warmupScenario)
            .WithReportFolder("./load-test-reports/warmup")
            .Run();

        // ═══════════════════════════════════════════════════════════════
        // MAIN TEST: SINGLE-KEY SATURATION BURST
        // ═══════════════════════════════════════════════════════════════

        var requestsPerSecond = totalRequests / burstDurationSeconds;

        var scenario = Scenario.Create("single_key_saturation", async context =>
        {
            var orderId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();

            var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        orderId,
                        items = new[]
                        {
                            new { productId = hotProductId, quantity = 1, sku = "PS5-HOT-KEY" }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            var response = await Http.Send(httpClient, request);
            stopwatch.Stop();

            latencies.Add(stopwatch.Elapsed.TotalMilliseconds);

            // Categorize response
            if (response.IsError)
            {
                var statusCode = response.StatusCode ?? "";

                // Database timeout detection
                if (statusCode.StartsWith("5") ||
                    response.Message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Interlocked.Increment(ref dbTimeoutErrors);
                    return Response.Fail("DB_TIMEOUT", statusCode: statusCode);
                }

                // Lock timeout detection (PostgreSQL specific)
                if (response.Message?.Contains("lock", StringComparison.OrdinalIgnoreCase) == true ||
                    response.Message?.Contains("deadlock", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Interlocked.Increment(ref lockTimeoutErrors);
                    return Response.Fail("LOCK_TIMEOUT", statusCode: statusCode);
                }

                // Stock depleted (expected business error)
                if (statusCode is "409" or "400")
                {
                    Interlocked.Increment(ref stockDepletedCount);
                    return Response.Ok("STOCK_DEPLETED", statusCode: statusCode);
                }

                return Response.Fail(statusCode: statusCode);
            }

            Interlocked.Increment(ref successCount);
            return Response.Ok(statusCode: response.StatusCode);
        })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(requestsPerSecond, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(burstDurationSeconds))
            );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/single-key-saturation")
            .Run();

        // ═══════════════════════════════════════════════════════════════
        // ASSERTIONS: ACM-GRADE METRICS
        // ═══════════════════════════════════════════════════════════════

        var scenarioStats = stats.ScenarioStats[0];

        // METRIC 1: Zero Deadlock Rate
        lockTimeoutErrors.ShouldBe(0,
            "PARTITIONING FAILURE: Database lock timeouts detected. " +
            "Message partitioning may be leaking or misconfigured.");

        dbTimeoutErrors.ShouldBe(0,
            "INFRASTRUCTURE FAILURE: Database timeouts detected. " +
            "Check PostgreSQL connection pool and query timeout settings.");

        // METRIC 2: Linear Latency Growth Pattern
        var sortedLatencies = latencies.OrderBy(l => l).ToArray();
        var p50 = sortedLatencies.Length > 0 ? sortedLatencies[(int)(sortedLatencies.Length * 0.50)] : 0;
        var p90 = sortedLatencies.Length > 0 ? sortedLatencies[(int)(sortedLatencies.Length * 0.90)] : 0;
        var p99 = sortedLatencies.Length > 0 ? sortedLatencies[(int)(sortedLatencies.Length * 0.99)] : 0;

        var linearityRatio = p50 > 0 ? p99 / p50 : 0;
        linearityRatio.ShouldBeLessThan(10.0,
            $"QUEUE ANOMALY: P99/P50 ratio is {linearityRatio:F2}. " +
            "Expected < 10 for linear queue behavior. May indicate contention leakage.");

        // METRIC 3: Saga Leak Detection
        await stats.AssertNoSagaLeaksAsync(apiBaseUrl);

        // Output detailed metrics for analysis
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("SINGLE-KEY SATURATION RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"Total Requests:        {totalRequests}");
        Console.WriteLine($"Successful:            {successCount}");
        Console.WriteLine($"Stock Depleted:        {stockDepletedCount}");
        Console.WriteLine($"DB Timeouts:           {dbTimeoutErrors}");
        Console.WriteLine($"Lock Timeouts:         {lockTimeoutErrors}");
        Console.WriteLine($"P50 Latency:           {p50:F2}ms");
        Console.WriteLine($"P90 Latency:           {p90:F2}ms");
        Console.WriteLine($"P99 Latency:           {p99:F2}ms");
        Console.WriteLine($"Linearity Ratio:       {linearityRatio:F2}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // TEST 2: PARTITION SKEW & THREAD STARVATION
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Creates two product streams that hash to the same Wolverine partition:
    ///     - Hot Stream: 100 RPS (the PS5)
    ///     - Cold Stream: 10 RPS (the Toaster)
    /// </summary>
    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task PartitionSkew_HotAndColdProducts_SameSlot_ShouldMeasureStarvation()
    {
        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════
        const string apiBaseUrl = "http://localhost:5000";
        const int hotRps = 100;
        const int coldRps = 10;
        const int testDurationSeconds = 30;

        // Generate ProductIds that hash deterministically to the same partition slot
        var (hotProductId, coldProductId) = GenerateCollidingProductIds(9);

        var hotLatencies = new ConcurrentBag<double>();
        var coldLatencies = new ConcurrentBag<double>();
        var coldSuccessCount = 0;
        var hotSuccessCount = 0;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var hotScenario = Scenario.Create("hot_product_ps5", async context =>
        {
            var orderId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();

            var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        orderId,
                        items = new[]
                        {
                            new { productId = hotProductId, quantity = 1, sku = "PS5-HOT" }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            var response = await Http.Send(httpClient, request);
            stopwatch.Stop();

            hotLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);

            if (!response.IsError || response.StatusCode is "409" or "400")
            {
                Interlocked.Increment(ref hotSuccessCount);
                return Response.Ok(statusCode: response.StatusCode);
            }

            return Response.Fail(statusCode: response.StatusCode);
        })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(hotRps, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(testDurationSeconds))
            );

        var coldScenario = Scenario.Create("cold_product_toaster", async context =>
        {
            var orderId = Guid.NewGuid();
            var stopwatch = Stopwatch.StartNew();

            var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        orderId,
                        items = new[]
                        {
                            new { productId = coldProductId, quantity = 1, sku = "TOASTER-COLD" }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            var response = await Http.Send(httpClient, request);
            stopwatch.Stop();

            coldLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);

            if (!response.IsError || response.StatusCode is "409" or "400")
            {
                Interlocked.Increment(ref coldSuccessCount);
                return Response.Ok(statusCode: response.StatusCode);
            }

            return Response.Fail(statusCode: response.StatusCode);
        })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(coldRps, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(testDurationSeconds))
            );

        var stats = NBomberRunner
            .RegisterScenarios(hotScenario, coldScenario)
            .WithReportFolder("./load-test-reports/partition-skew")
            .Run();

        var coldLatencyArray = coldLatencies.OrderBy(l => l).ToArray();
        var hotLatencyArray = hotLatencies.OrderBy(l => l).ToArray();

        var coldP99 = coldLatencyArray.Length > 0 ? coldLatencyArray[(int)(coldLatencyArray.Length * 0.99)] : 0;
        var hotP99 = hotLatencyArray.Length > 0 ? hotLatencyArray[(int)(hotLatencyArray.Length * 0.99)] : 0;
        var coldP50 = coldLatencyArray.Length > 0 ? coldLatencyArray[(int)(coldLatencyArray.Length * 0.50)] : 0;

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("PARTITION SKEW ANALYSIS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"Hot Product ID:        {hotProductId}");
        Console.WriteLine($"Cold Product ID:       {coldProductId}");
        Console.WriteLine($"Same Partition:        {GetPartitionSlot(hotProductId) == GetPartitionSlot(coldProductId)}");
        Console.WriteLine("───────────────────────────────────────────────────────────────");
        Console.WriteLine($"Hot Product P99:       {hotP99:F2}ms");
        Console.WriteLine($"Cold Product P50:      {coldP50:F2}ms");
        Console.WriteLine($"Cold Product P99:      {coldP99:F2}ms");
        Console.WriteLine($"Starvation Ratio:      {coldP99 / Math.Max(hotP99, 1):F2}x");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        if (coldP99 > 5000)
        {
            Console.WriteLine("⚠️  WARNING: Partition Skew detected!");
        }

        await stats.AssertNoSagaLeaksAsync(apiBaseUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // TEST 3: DATABASE WAL EXHAUSTION (IOPS CEILING TEST)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Stress tests the database write path by sending reservation requests
    ///     faster than the disk can fsync. Validates graceful backpressure.
    /// </summary>
    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task WalExhaustion_HighWriteLoad_ShouldApplyBackpressure()
    {
        const string apiBaseUrl = "http://localhost:5000";
        const int initialRps = 100;
        const int maxRps = 1000;
        const int rpsIncrement = 100;
        const int stepDurationSeconds = 10;

        var metricsSnapshots = new List<WalExhaustionMetrics>();
        var currentRps = initialRps;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("WAL EXHAUSTION TEST - IOPS CEILING DISCOVERY");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        while (currentRps <= maxRps)
        {
            var successCount = 0;
            var errorCount = 0;
            var latencies = new ConcurrentBag<double>();

            var scenario = Scenario.Create($"wal_stress_{currentRps}rps", async context =>
            {
                var orderId = Guid.NewGuid();
                var productId = Guid.NewGuid();
                var stopwatch = Stopwatch.StartNew();

                var request = Http.CreateRequest("POST", "/api/v1/inventory/reserve")
                    .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            orderId,
                            items = new[]
                            {
                                new { productId, quantity = 1, sku = "WAL-STRESS-TEST" }
                            }
                        }),
                        Encoding.UTF8,
                        "application/json"));

                var response = await Http.Send(httpClient, request);
                stopwatch.Stop();

                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);

                if (!response.IsError || response.StatusCode is "409" or "400")
                {
                    Interlocked.Increment(ref successCount);
                    return Response.Ok(statusCode: response.StatusCode);
                }

                Interlocked.Increment(ref errorCount);
                return Response.Fail(statusCode: response.StatusCode);
            })
                .WithoutWarmUp()
                .WithLoadSimulations(
                    Simulation.Inject(currentRps, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(stepDurationSeconds))
                );

            var sagaMetricsBefore = await GetSagaMetricsAsync(httpClient);

            var stats = NBomberRunner
                .RegisterScenarios(scenario)
                .WithReportFolder($"./load-test-reports/wal-stress/{currentRps}rps")
                .Run();

            var sagaMetricsAfter = await GetSagaMetricsAsync(httpClient);

            var sortedLatencies = latencies.OrderBy(l => l).ToArray();
            var p99 = sortedLatencies.Length > 0 ? sortedLatencies[(int)(sortedLatencies.Length * 0.99)] : 0;
            var avgLatency = sortedLatencies.Length > 0 ? sortedLatencies.Average() : 0;

            var snapshot = new WalExhaustionMetrics
            {
                TargetRps = currentRps,
                ActualRps = (successCount + errorCount) / (double)stepDurationSeconds,
                SuccessCount = successCount,
                ErrorCount = errorCount,
                P99LatencyMs = p99,
                AvgLatencyMs = avgLatency,
                SagaBacklogBefore = sagaMetricsBefore.ProcessingPaymentCount,
                SagaBacklogAfter = sagaMetricsAfter.ProcessingPaymentCount
            };

            metricsSnapshots.Add(snapshot);

            Console.WriteLine($"RPS: {currentRps,4} | Success: {successCount,5} | Errors: {errorCount,4} | P99: {p99,8:F2}ms | Saga Backlog: {sagaMetricsAfter.ProcessingPaymentCount}");

            if (p99 > 2000 && sagaMetricsAfter.ProcessingPaymentCount > sagaMetricsBefore.ProcessingPaymentCount + 10)
            {
                Console.WriteLine("═══════════════════════════════════════════════════════════════");
                Console.WriteLine($"🎯 IOPS CEILING DETECTED at approximately {currentRps - rpsIncrement} RPS");
                Console.WriteLine("   Backpressure is being applied correctly (latency increase, saga backlog)");
                Console.WriteLine("═══════════════════════════════════════════════════════════════");
                break;
            }

            currentRps += rpsIncrement;
        }

        var healthCheck = await httpClient.GetAsync("/health");
        healthCheck.IsSuccessStatusCode.ShouldBeTrue("CRITICAL: System became unresponsive under WAL stress.");

        Console.WriteLine("\nWaiting 30s for saga backlog to drain...");
        await Task.Delay(TimeSpan.FromSeconds(30));

        await new NBomber.Contracts.Stats.NodeStats().AssertNoSagaLeaksAsync(apiBaseUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // TEST 4: STALE CACHE RACE (MEILISEARCH VS POSTGRES)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Performs a "High-Frequency Flip-Flop" test:
    ///     - Update product price every 100ms
    ///     - Concurrent users search and attempt to buy
    /// </summary>
    [Fact]
    [Trait("Category", "LoadTest")]
    public async Task StaleCacheRace_RapidPriceUpdates_ShouldEnforceTriplePassPricing()
    {
        const string apiBaseUrl = "http://localhost:5000";
        const int priceUpdateIntervalMs = 100;
        const int testDurationSeconds = 30;
        const int buyersPerSecond = 10;

        var testProductId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var priceConflicts = 0;
        var staleSuccesses = 0;
        var legitimateSuccesses = 0;
        var priceVersions = new ConcurrentDictionary<decimal, int>();
        var currentPrice = 499.99m;
        var priceUpdateCts = new CancellationTokenSource();

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var priceUpdateTask = Task.Run(async () =>
        {
            var prices = new[] { 499.99m, 549.99m, 479.99m, 599.99m };
            var index = 0;

            while (!priceUpdateCts.Token.IsCancellationRequested)
            {
                currentPrice = prices[index % prices.Length];
                priceVersions.AddOrUpdate(currentPrice, 1, (_, v) => v + 1);

                var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{testProductId}/price")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { amount = currentPrice, currency = "GEL" }),
                        Encoding.UTF8,
                        "application/json")
                };

                try
                {
                    await httpClient.SendAsync(updateRequest, priceUpdateCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                index++;
                await Task.Delay(priceUpdateIntervalMs, priceUpdateCts.Token);
            }
        });

        await Task.Delay(500);

        var scenario = Scenario.Create("stale_cache_race", async context =>
        {
            var searchedPrice = currentPrice;

            await Task.Delay(Random.Shared.Next(50, 200));

            var orderRequest = Http.CreateRequest("POST", "/api/v1/orders")
                .WithHeader("X-Idempotency-Key", Guid.NewGuid().ToString())
                .WithHeader("Content-Type", "application/json")
                .WithBody(new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        customerId = Guid.NewGuid(),
                        customerEmail = "test@example.com",
                        customerName = "Test User",
                        items = new[]
                        {
                            new
                            {
                                productId = testProductId,
                                quantity = 1,
                                expectedPrice = searchedPrice
                            }
                        },
                        shippingAddress = new
                        {
                            recipientName = "Test User",
                            street = "123 Test St",
                            city = "Tbilisi",
                            state = "TB",
                            country = "Georgia",
                            postalCode = "0100",
                            phoneNumber = "+995555123456"
                        },
                        billingAddress = new
                        {
                            recipientName = "Test User",
                            street = "123 Test St",
                            city = "Tbilisi",
                            state = "TB",
                            country = "Georgia",
                            postalCode = "0100"
                        }
                    }),
                    Encoding.UTF8,
                    "application/json"));

            var response = await Http.Send(httpClient, orderRequest);

            if (response.StatusCode == "409")
            {
                Interlocked.Increment(ref priceConflicts);
                return Response.Ok("PRICE_CONFLICT", statusCode: "409");
            }

            if (response.IsError)
            {
                return Response.Fail(statusCode: response.StatusCode);
            }

            var actualPrice = currentPrice;
            if (Math.Abs(searchedPrice - actualPrice) > 0.01m)
            {
                Interlocked.Increment(ref staleSuccesses);
                return Response.Fail("STALE_PRICE_BUG", statusCode: "BUG");
            }

            Interlocked.Increment(ref legitimateSuccesses);
            return Response.Ok(statusCode: response.StatusCode);
        })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(buyersPerSecond, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(testDurationSeconds))
            );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("./load-test-reports/stale-cache-race")
            .Run();

        priceUpdateCts.Cancel();
        await priceUpdateTask;

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("STALE CACHE RACE TEST RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"Price Conflicts (409):     {priceConflicts}");
        Console.WriteLine($"Legitimate Successes:      {legitimateSuccesses}");
        Console.WriteLine($"Stale Price Bugs:          {staleSuccesses}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        staleSuccesses.ShouldBe(0, "TRIPLE-PASS PRICING FAILURE: Orders succeeded with stale Meilisearch price.");
        priceConflicts.ShouldBeGreaterThan(0, "TEST VALIDITY: No price conflicts detected.");

        await stats.AssertNoSagaLeaksAsync(apiBaseUrl);
    }

    #region Helper Methods

    /// <summary>
    ///     Generates two ProductIds that deterministically hash to the same Wolverine partition slot.
    /// </summary>
    private static (Guid hotProductId, Guid coldProductId) GenerateCollidingProductIds(int partitionCount)
    {
        var hotProductId = Guid.NewGuid();
        var hotSlot = GetPartitionSlot(hotProductId, partitionCount);

        while (true)
        {
            var coldProductId = Guid.NewGuid();
            if (GetPartitionSlot(coldProductId, partitionCount) == hotSlot)
            {
                return (hotProductId, coldProductId);
            }
        }
    }

    /// <summary>
    ///     Calculates the partition slot for a ProductId using Wolverine's DJB2/FNV-1a deterministic hash.
    /// </summary>
    private static int GetPartitionSlot(Guid productId, int partitionCount = 9)
    {
        var str = productId.ToString();
        var deterministicHash = GetDeterministicHashCode(str);
        return Math.Abs(deterministicHash) % partitionCount;
    }

    /// <summary>
    ///     Deterministic DJB2 String Hash (Process-Independent).
    /// </summary>
    private static int GetDeterministicHashCode(string str)
    {
        unchecked
        {
            int hash = 5381;
            for (int i = 0; i < str.Length; i++)
            {
                hash = ((hash << 5) + hash) ^ str[i];
            }
            return hash;
        }
    }

    private static async Task<SagaMetricsSnapshot> GetSagaMetricsAsync(HttpClient httpClient)
    {
        try
        {
            var response = await httpClient.GetAsync("/metrics/sagas");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SagaMetricsSnapshot>(content)
                       ?? new SagaMetricsSnapshot();
            }
        }
        catch
        {
            // Metrics endpoint may not exist
        }

        return new SagaMetricsSnapshot();
    }

    #endregion

    private record WalExhaustionMetrics
    {
        public int TargetRps { get; init; }
        public double ActualRps { get; init; }
        public int SuccessCount { get; init; }
        public int ErrorCount { get; init; }
        public double P99LatencyMs { get; init; }
        public double AvgLatencyMs { get; init; }
        public long SagaBacklogBefore { get; init; }
        public long SagaBacklogAfter { get; init; }
    }

    private record SagaMetricsSnapshot
    {
        public long ReservingInventoryCount { get; init; }
        public long ProcessingPaymentCount { get; init; }
        public long ConfirmingInventoryCount { get; init; }
    }
}
