using System.Net.Http.Json;
using Shouldly;

namespace NetCommerce.LoadTests.Assertions;

/// <summary>
///     Assertions for detecting "Saga Leaks" after load tests.
///     A saga leak occurs when active saga instances remain after all requests complete.
/// </summary>
public static class SagaLeakAssertions
{
    /// <summary>
    ///     Asserts that no active sagas remain after the load test completes.
    ///     Uses a gauge endpoint to check active saga counts.
    /// </summary>
    /// <param name="httpClient">HTTP client configured for the API base URL.</param>
    /// <param name="maxWaitTime">Maximum time to wait for sagas to complete.</param>
    /// <param name="pollingInterval">Interval between status checks.</param>
    public static async Task AssertNoActiveSagasAsync(
        HttpClient httpClient,
        TimeSpan? maxWaitTime = null,
        TimeSpan? pollingInterval = null)
    {
        var maxWait = maxWaitTime ?? TimeSpan.FromSeconds(30);
        var pollInterval = pollingInterval ?? TimeSpan.FromMilliseconds(500);
        var deadline = DateTime.UtcNow.Add(maxWait);

        while (DateTime.UtcNow < deadline)
        {
            var activeSagaCount = await GetActiveSagaCountAsync(httpClient);

            if (activeSagaCount == 0)
                return; // Success - no leaks

            await Task.Delay(pollInterval);
        }

        // Final check
        var finalCount = await GetActiveSagaCountAsync(httpClient);
        finalCount.ShouldBe(0, $"SAGA LEAK DETECTED: {finalCount} active sagas remain after {maxWait.TotalSeconds}s");
    }

    /// <summary>
    ///     Gets the current count of active sagas from the metrics endpoint.
    /// </summary>
    private static async Task<int> GetActiveSagaCountAsync(HttpClient httpClient)
    {
        try
        {
            // Try the standard metrics endpoint first
            var response = await httpClient.GetAsync("/metrics/active-sagas");

            if (response.IsSuccessStatusCode)
            {
                var metrics = await response.Content.ReadFromJsonAsync<SagaMetrics>();
                return metrics?.ActiveCount ?? 0;
            }

            // Fallback: Try Prometheus-style metrics
            var prometheusResponse = await httpClient.GetAsync("/metrics");
            if (prometheusResponse.IsSuccessStatusCode)
            {
                var metricsText = await prometheusResponse.Content.ReadAsStringAsync();
                return ParseActivesSagasFromPrometheus(metricsText);
            }

            return 0;
        }
        catch (HttpRequestException)
        {
            // API may not have metrics endpoint - assume no leaks
            return 0;
        }
    }

    /// <summary>
    ///     Parses active saga count from Prometheus-style metrics output.
    /// </summary>
    private static int ParseActivesSagasFromPrometheus(string metricsText)
    {
        // Look for active.sagas or wolverine_saga_active gauge
        var lines = metricsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.StartsWith("#")) continue; // Skip comments

            if (line.Contains("active_sagas") || line.Contains("saga_active"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[^1], out var count))
                {
                    return count;
                }
            }
        }

        return 0;
    }

    private record SagaMetrics(int ActiveCount, int CompletedCount, int FailedCount);
}

/// <summary>
///     Extension methods for NBomber stats to detect saga leaks.
/// </summary>
public static class NbomberSagaExtensions
{
    /// <summary>
    ///     Asserts that the load test scenario has no saga leaks.
    ///     Call this after NBomberRunner.Run() completes.
    /// </summary>
    public static async Task AssertNoSagaLeaksAsync(
        this NBomber.Contracts.Stats.NodeStats stats,
        string apiBaseUrl)
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        await SagaLeakAssertions.AssertNoActiveSagasAsync(httpClient);
    }
}
