#region

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Resilience;

/// <summary>
///     2025 Degraded Mode Service: Adaptive system behavior based on external service health.
///     Elite Pattern: "If the Shipping Provider API has a >20% failure rate,
///     the UI should automatically change the 'Estimated Delivery' from '2 days' to '5-7 days'
///     before the user even buys. This is true Operational Resilience."
///     Uses Redis as a distributed feature flag store, allowing all instances to coordinate
///     degraded mode state without requiring a deployment.
/// </summary>
public interface IDegradedModeService
{
    /// <summary>
    ///     Check if a specific service is in degraded mode.
    /// </summary>
    Task<bool> IsServiceDegradedAsync(string serviceName, CancellationToken ct = default);

    /// <summary>
    ///     Get degraded mode configuration for a service (delivery estimates, retry limits, etc.).
    /// </summary>
    Task<DegradedModeConfig?> GetDegradedModeConfigAsync(string serviceName, CancellationToken ct = default);

    /// <summary>
    ///     Enable degraded mode for a service (called by monitoring system or admin API).
    /// </summary>
    Task EnableDegradedModeAsync(
        string serviceName,
        DegradedModeConfig config,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    ///     Disable degraded mode for a service (service health restored).
    /// </summary>
    Task DisableDegradedModeAsync(string serviceName, CancellationToken ct = default);

    /// <summary>
    ///     Record a service health check result (used to automatically trigger degraded mode).
    /// </summary>
    Task RecordHealthCheckAsync(
        string serviceName,
        bool isHealthy,
        double responseTimeMs,
        CancellationToken ct = default);

    /// <summary>
    ///     Get current health metrics for a service (failure rate, avg response time).
    /// </summary>
    Task<ServiceHealthMetrics> GetHealthMetricsAsync(string serviceName, CancellationToken ct = default);
}

/// <summary>
///     Degraded mode configuration (stored in Redis).
/// </summary>
public record DegradedModeConfig
{
    /// <summary>
    ///     User-facing message to display in UI.
    ///     Example: "Due to high demand, delivery times may be extended."
    /// </summary>
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>
    ///     Adjusted delivery estimate (days).
    ///     Example: Normal = 2 days, Degraded = 5-7 days
    /// </summary>
    public int DeliveryEstimateDaysMin { get; init; }

    public int DeliveryEstimateDaysMax { get; init; }

    /// <summary>
    ///     Feature flags to disable during degraded mode.
    ///     Example: ["ExpressShipping", "SameDayDelivery"]
    /// </summary>
    public List<string> DisabledFeatures { get; init; } = new();

    /// <summary>
    ///     Reduced retry limits for external API calls.
    ///     Example: Normal = 3 retries, Degraded = 1 retry (fail fast)
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 1;

    /// <summary>
    ///     When degraded mode was activated.
    /// </summary>
    public DateTimeOffset ActivatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Reason for degraded mode (for audit trail).
    ///     Example: "Shipping provider API failure rate >20%"
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
///     Real-time health metrics for a service (rolling 5-minute window).
/// </summary>
public record ServiceHealthMetrics
{
    public string ServiceName { get; init; } = string.Empty;
    public int TotalRequests { get; init; }
    public int FailedRequests { get; init; }
    public double FailureRate => TotalRequests > 0 ? (double)FailedRequests / TotalRequests : 0;
    public double AverageResponseTimeMs { get; init; }
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
}

/// <summary>
///     Redis-based implementation of Degraded Mode Service.
/// </summary>
public sealed class RedisDegradedModeService : IDegradedModeService
{
    private const string DegradedModeKeyPrefix = "degraded-mode:";
    private const string HealthMetricsKeyPrefix = "health-metrics:";

    // Thresholds for auto-triggering degraded mode
    private const double FailureRateThreshold = 0.20; // 20% failure rate
    private const double ResponseTimeThreshold = 5000; // 5 seconds avg response time
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
    private readonly TimeSpan _healthMetricsWindow = TimeSpan.FromMinutes(5);
    private readonly ILogger<RedisDegradedModeService> _logger;

    public RedisDegradedModeService(
        IDistributedCache cache,
        ILogger<RedisDegradedModeService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsServiceDegradedAsync(string serviceName, CancellationToken ct = default)
    {
        string key = $"{DegradedModeKeyPrefix}{serviceName}";
        string? value = await _cache.GetStringAsync(key, ct);
        return !string.IsNullOrEmpty(value);
    }

    public async Task<DegradedModeConfig?> GetDegradedModeConfigAsync(
        string serviceName,
        CancellationToken ct = default)
    {
        string key = $"{DegradedModeKeyPrefix}{serviceName}";
        string? value = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrEmpty(value))
            return null;

        return JsonSerializer.Deserialize<DegradedModeConfig>(value);
    }

    public async Task EnableDegradedModeAsync(
        string serviceName,
        DegradedModeConfig config,
        string reason,
        CancellationToken ct = default)
    {
        string key = $"{DegradedModeKeyPrefix}{serviceName}";
        DegradedModeConfig configWithReason = config with { Reason = reason, ActivatedAt = DateTimeOffset.UtcNow };
        string value = JsonSerializer.Serialize(configWithReason);

        await _cache.SetStringAsync(
            key,
            value,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _cacheExpiration },
            ct);

        _logger.LogWarning(
            "DEGRADED MODE ENABLED: {ServiceName}. Reason: {Reason}. Config: {@Config}",
            serviceName, reason, configWithReason);

        // Add distributed tracing activity
        Activity.Current?.AddTag("degraded_mode.service", serviceName);
        Activity.Current?.AddTag("degraded_mode.reason", reason);
    }

    public async Task DisableDegradedModeAsync(string serviceName, CancellationToken ct = default)
    {
        string key = $"{DegradedModeKeyPrefix}{serviceName}";
        await _cache.RemoveAsync(key, ct);

        _logger.LogInformation(
            "DEGRADED MODE DISABLED: {ServiceName}. Service health restored.",
            serviceName);

        Activity.Current?.AddTag("degraded_mode.service", serviceName);
        Activity.Current?.AddTag("degraded_mode.restored", true);
    }

    public async Task RecordHealthCheckAsync(
        string serviceName,
        bool isHealthy,
        double responseTimeMs,
        CancellationToken ct = default)
    {
        // Get current health metrics
        ServiceHealthMetrics metrics = await GetHealthMetricsAsync(serviceName, ct);

        // Update metrics
        var updatedMetrics = new ServiceHealthMetrics
        {
            ServiceName = serviceName,
            TotalRequests = metrics.TotalRequests + 1,
            FailedRequests = metrics.FailedRequests + (isHealthy ? 0 : 1),
            AverageResponseTimeMs = (metrics.AverageResponseTimeMs * metrics.TotalRequests + responseTimeMs)
                                    / (metrics.TotalRequests + 1),
            WindowStart = metrics.WindowStart == default ? DateTimeOffset.UtcNow : metrics.WindowStart,
            WindowEnd = DateTimeOffset.UtcNow
        };

        // Store updated metrics
        string key = $"{HealthMetricsKeyPrefix}{serviceName}";
        string value = JsonSerializer.Serialize(updatedMetrics);
        await _cache.SetStringAsync(
            key,
            value,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _healthMetricsWindow },
            ct);

        // Auto-trigger degraded mode if thresholds exceeded
        await CheckAndTriggerDegradedModeAsync(serviceName, updatedMetrics, ct);
    }

    public async Task<ServiceHealthMetrics> GetHealthMetricsAsync(
        string serviceName,
        CancellationToken ct = default)
    {
        string key = $"{HealthMetricsKeyPrefix}{serviceName}";
        string? value = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrEmpty(value))
            return new ServiceHealthMetrics
            {
                ServiceName = serviceName,
                TotalRequests = 0,
                FailedRequests = 0,
                AverageResponseTimeMs = 0,
                WindowStart = DateTimeOffset.UtcNow,
                WindowEnd = DateTimeOffset.UtcNow
            };

        return JsonSerializer.Deserialize<ServiceHealthMetrics>(value) ??
               new ServiceHealthMetrics { ServiceName = serviceName };
    }

    /// <summary>
    ///     Automatically trigger degraded mode if failure rate or response time exceeds thresholds.
    ///     Elite 2025 Pattern: Self-healing system that adapts to external service degradation.
    /// </summary>
    private async Task CheckAndTriggerDegradedModeAsync(
        string serviceName,
        ServiceHealthMetrics metrics,
        CancellationToken ct)
    {
        // Need minimum sample size to avoid false positives
        if (metrics.TotalRequests < 10)
            return;

        // Check if already in degraded mode
        if (await IsServiceDegradedAsync(serviceName, ct))
        {
            // Check if health restored (auto-disable degraded mode)
            if (metrics.FailureRate < 0.05 && metrics.AverageResponseTimeMs < 2000)
                await DisableDegradedModeAsync(serviceName, ct);
            return;
        }

        // Trigger degraded mode if failure rate exceeds threshold
        if (metrics.FailureRate >= FailureRateThreshold)
        {
            DegradedModeConfig config = CreateDegradedModeConfig(serviceName, metrics);
            await EnableDegradedModeAsync(
                serviceName,
                config,
                $"Failure rate {metrics.FailureRate:P1} exceeds threshold {FailureRateThreshold:P1}",
                ct);
        }

        // Trigger degraded mode if response time exceeds threshold
        else if (metrics.AverageResponseTimeMs >= ResponseTimeThreshold)
        {
            DegradedModeConfig config = CreateDegradedModeConfig(serviceName, metrics);
            await EnableDegradedModeAsync(
                serviceName,
                config,
                $"Avg response time {metrics.AverageResponseTimeMs:F0}ms exceeds threshold {ResponseTimeThreshold}ms",
                ct);
        }
    }

    /// <summary>
    ///     Create degraded mode configuration based on service type.
    /// </summary>
    private DegradedModeConfig CreateDegradedModeConfig(string serviceName, ServiceHealthMetrics metrics)
    {
        return serviceName switch
        {
            "ShippingProvider" => new DegradedModeConfig
            {
                UserMessage = "Due to carrier delays, delivery times may be extended by 3-5 days.",
                DeliveryEstimateDaysMin = 5,
                DeliveryEstimateDaysMax = 7,
                DisabledFeatures = new List<string> { "ExpressShipping", "SameDayDelivery" },
                MaxRetryAttempts = 1 // Fail fast
            },
            "PaymentGateway" => new DegradedModeConfig
            {
                UserMessage = "Payment processing may take longer than usual. Please be patient.",
                DeliveryEstimateDaysMin = 2,
                DeliveryEstimateDaysMax = 3,
                DisabledFeatures = new List<string> { "SavePaymentMethod", "OneClickCheckout" },
                MaxRetryAttempts = 2
            },
            "InventorySync" => new DegradedModeConfig
            {
                UserMessage = "Inventory data may be slightly delayed. Stock levels are approximate.",
                DeliveryEstimateDaysMin = 2,
                DeliveryEstimateDaysMax = 3,
                DisabledFeatures = new List<string> { "ReserveInventory", "BackorderNotifications" },
                MaxRetryAttempts = 5 // High retries for eventual consistency
            },
            _ => new DegradedModeConfig
            {
                UserMessage = "Some features may be temporarily unavailable.",
                DeliveryEstimateDaysMin = 3,
                DeliveryEstimateDaysMax = 5,
                DisabledFeatures = new List<string>(),
                MaxRetryAttempts = 1
            }
        };
    }
}

/// <summary>
///     Extension methods for dependency injection.
/// </summary>
public static class DegradedModeServiceExtensions
{
    public static IServiceCollection AddDegradedModeService(this IServiceCollection services)
    {
        services.AddSingleton<IDegradedModeService, RedisDegradedModeService>();
        return services;
    }
}
