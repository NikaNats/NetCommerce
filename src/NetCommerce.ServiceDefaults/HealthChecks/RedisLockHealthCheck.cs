#nullable enable
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace NetCommerce.ServiceDefaults.HealthChecks;

/// <summary>
///     Redis health check that verifies connectivity and lock acquisition capability.
///
///     <para>
///     <b>Purpose:</b> Critical for the Inventory module which uses Redis for distributed locking.
///     When Redis is unhealthy, the system should return 503 Service Unavailable rather than
///     allowing un-locked reservations that could cause overselling.
///     </para>
///
///     <para>
///     <b>Checks performed:</b>
///     1. Basic PING connectivity
///     2. SET/GET operation (verifies write capability)
///     3. Optional: Lock acquisition test (verifies locking works)
///     </para>
/// </summary>
public sealed class RedisLockHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer? _connectionMultiplexer;
    private readonly IDistributedCache? _distributedCache;
    private readonly ILogger<RedisLockHealthCheck>? _logger;
    private readonly RedisLockHealthCheckOptions _options;

    public RedisLockHealthCheck(
        RedisLockHealthCheckOptions options,
        IConnectionMultiplexer? connectionMultiplexer = null,
        IDistributedCache? distributedCache = null,
        ILogger<RedisLockHealthCheck>? logger = null)
    {
        _options = options;
        _connectionMultiplexer = connectionMultiplexer;
        _distributedCache = distributedCache;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Method 1: Direct IConnectionMultiplexer (preferred)
            if (_connectionMultiplexer != null)
            {
                return await CheckWithConnectionMultiplexerAsync(cancellationToken);
            }

            // Method 2: IDistributedCache fallback
            if (_distributedCache != null)
            {
                return await CheckWithDistributedCacheAsync(cancellationToken);
            }

            return HealthCheckResult.Unhealthy(
                "No Redis connection available (neither IConnectionMultiplexer nor IDistributedCache registered)");
        }
        catch (RedisConnectionException ex)
        {
            _logger?.LogError(ex, "[RedisHealth] Redis connection failed - potential overselling risk!");
            return HealthCheckResult.Unhealthy(
                "Redis connection failed. CRITICAL: Inventory module cannot acquire distributed locks!",
                ex,
                new Dictionary<string, object>
                {
                    ["error_type"] = "ConnectionFailed",
                    ["impact"] = "Inventory reservations may cause overselling"
                });
        }
        catch (RedisTimeoutException ex)
        {
            _logger?.LogWarning(ex, "[RedisHealth] Redis timeout - degraded locking performance");
            return HealthCheckResult.Degraded(
                "Redis timeout. Lock acquisition may be slow.",
                ex,
                new Dictionary<string, object>
                {
                    ["error_type"] = "Timeout",
                    ["timeout_ms"] = _options.TimeoutMs
                });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[RedisHealth] Unexpected Redis health check failure");
            return HealthCheckResult.Unhealthy(
                $"Redis health check failed: {ex.Message}",
                ex);
        }
    }

    private async Task<HealthCheckResult> CheckWithConnectionMultiplexerAsync(CancellationToken cancellationToken)
    {
        var db = _connectionMultiplexer!.GetDatabase();
        var testKey = $"health:lock:test:{Guid.NewGuid():N}";
        var testValue = DateTime.UtcNow.ToString("O");

        // Test 1: PING
        var pingResult = await db.PingAsync();
        if (pingResult > TimeSpan.FromMilliseconds(_options.TimeoutMs))
        {
            return HealthCheckResult.Degraded(
                $"Redis PING latency is high: {pingResult.TotalMilliseconds:F2}ms",
                data: new Dictionary<string, object>
                {
                    ["ping_latency_ms"] = pingResult.TotalMilliseconds
                });
        }

        // Test 2: SET/GET (verifies write capability)
        var setResult = await db.StringSetAsync(
            testKey,
            testValue,
            TimeSpan.FromSeconds(5),
            When.Always,
            CommandFlags.None);

        if (!setResult)
        {
            return HealthCheckResult.Unhealthy("Redis SET operation failed - write capability compromised");
        }

        var getResult = await db.StringGetAsync(testKey);
        if (getResult != testValue)
        {
            return HealthCheckResult.Unhealthy("Redis GET returned unexpected value - data integrity issue");
        }

        // Cleanup
        await db.KeyDeleteAsync(testKey);

        // Test 3: Lock acquisition (if enabled)
        if (_options.TestLockAcquisition)
        {
            var lockKey = $"health:lock:acquire:{Guid.NewGuid():N}";
            var lockToken = Guid.NewGuid().ToString();

            var lockAcquired = await db.LockTakeAsync(
                lockKey,
                lockToken,
                TimeSpan.FromSeconds(5));

            if (!lockAcquired)
            {
                return HealthCheckResult.Unhealthy(
                    "Redis lock acquisition failed - distributed locking compromised");
            }

            await db.LockReleaseAsync(lockKey, lockToken);
        }

        return HealthCheckResult.Healthy(
            $"Redis healthy (PING: {pingResult.TotalMilliseconds:F2}ms)",
            new Dictionary<string, object>
            {
                ["ping_latency_ms"] = pingResult.TotalMilliseconds,
                ["server_version"] = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints()[0]).Version.ToString()
            });
    }

    private async Task<HealthCheckResult> CheckWithDistributedCacheAsync(CancellationToken cancellationToken)
    {
        var testKey = $"health:distributed:{Guid.NewGuid():N}";
        var testValue = System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O"));

        // Test SET
        await _distributedCache!.SetAsync(
            testKey,
            testValue,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
            },
            cancellationToken);

        // Test GET
        var result = await _distributedCache.GetAsync(testKey, cancellationToken);
        if (result == null || !result.SequenceEqual(testValue))
        {
            return HealthCheckResult.Unhealthy("Distributed cache GET returned unexpected value");
        }

        // Cleanup
        await _distributedCache.RemoveAsync(testKey, cancellationToken);

        return HealthCheckResult.Healthy("Distributed cache healthy");
    }
}

/// <summary>
///     Options for the Redis lock health check.
/// </summary>
public sealed class RedisLockHealthCheckOptions
{
    /// <summary>
    ///     Timeout for Redis operations in milliseconds. Default: 1000ms
    /// </summary>
    public int TimeoutMs { get; set; } = 1000;

    /// <summary>
    ///     Whether to test actual lock acquisition. Default: true
    ///     This adds ~5ms overhead but verifies locking capability.
    /// </summary>
    public bool TestLockAcquisition { get; set; } = true;
}
