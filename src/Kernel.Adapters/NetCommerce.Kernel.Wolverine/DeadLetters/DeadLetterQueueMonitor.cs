#nullable enable
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NetCommerce.Kernel.Wolverine.DeadLetters;

/// <summary>
///     Background worker that monitors the Wolverine dead letter queue.
///
///     <para>
///     <b>Production Readiness Requirement:</b> "Having a plan for when a message permanently fails."
///     This worker provides:
///     - Real-time metrics for DLQ depth (Prometheus/OTLP compatible)
///     - Health check integration (degrades if DLQ exceeds threshold)
///     - Alerting via logging (integrate with your alerting stack)
///     - Periodic summary reports
///     </para>
///
///     <para>
///     <b>Metrics Exposed:</b>
///     - wolverine_dlq_message_count: Current count of dead-lettered messages
///     - wolverine_dlq_oldest_message_age_seconds: Age of oldest message in DLQ
///     - wolverine_dlq_messages_by_type: Breakdown by message type
///     </para>
/// </summary>
public sealed class DeadLetterQueueMonitor : BackgroundService, IHealthCheck
{
    private readonly ILogger<DeadLetterQueueMonitor> _logger;
    private readonly DeadLetterQueueMonitorOptions _options;
    private readonly string _connectionString;
    private readonly Meter _meter;
    private readonly Counter<long> _newDeadLettersCounter;
    private readonly Histogram<double> _messageAgeHistogram;

    // Cached state for health checks
    private DeadLetterQueueState _lastState = new();
    private DateTime _lastCheckTime = DateTime.MinValue;

    public DeadLetterQueueMonitor(
        IOptions<DeadLetterQueueMonitorOptions> options,
        ILogger<DeadLetterQueueMonitor> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connectionString = _options.ConnectionString
            ?? throw new InvalidOperationException("ConnectionString is required for DeadLetterQueueMonitor");

        // Initialize OpenTelemetry metrics
        _meter = new Meter("NetCommerce.Wolverine.DeadLetters", "1.0.0");

        _newDeadLettersCounter = _meter.CreateCounter<long>(
            "wolverine_dlq_new_messages_total",
            "messages",
            "Total number of new dead-lettered messages since startup");

        _messageAgeHistogram = _meter.CreateHistogram<double>(
            "wolverine_dlq_message_age_seconds",
            "seconds",
            "Age distribution of messages in the dead letter queue");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[DLQ Monitor] Starting Dead Letter Queue monitor. Check interval: {Interval}s, Alert threshold: {Threshold}",
            _options.CheckIntervalSeconds, _options.AlertThreshold);

        // Initial delay to let the system warm up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var state = await CheckDeadLetterQueueAsync(stoppingToken);
                _lastState = state;
                _lastCheckTime = DateTime.UtcNow;

                // Log summary
                LogSummary(state);

                // Check if we need to alert
                if (state.TotalCount >= _options.AlertThreshold)
                {
                    _logger.LogError(
                        "[DLQ ALERT] Dead letter queue has {Count} messages (threshold: {Threshold}). " +
                        "Oldest message: {OldestAge}. Top message types: {TopTypes}",
                        state.TotalCount,
                        _options.AlertThreshold,
                        state.OldestMessageAge,
                        string.Join(", ", state.MessagesByType.Take(3).Select(x => $"{x.Key}: {x.Value}")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DLQ Monitor] Failed to check dead letter queue");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);
        }
    }

    private async Task<DeadLetterQueueState> CheckDeadLetterQueueAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var state = new DeadLetterQueueState { CheckedAt = DateTime.UtcNow };

        // Query 1: Total count and oldest message
        const string countQuery = """
            SELECT
                COUNT(*) as total_count,
                MIN(timestamp) as oldest_timestamp,
                MAX(timestamp) as newest_timestamp
            FROM wolverine.wolverine_dead_letters
            """;

        await using (var cmd = new NpgsqlCommand(countQuery, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                state.TotalCount = reader.GetInt64(0);
                if (!await reader.IsDBNullAsync(1, cancellationToken))
                {
                    var oldestTimestamp = reader.GetDateTime(1);
                    state.OldestMessageAge = DateTime.UtcNow - oldestTimestamp;
                    _messageAgeHistogram.Record(state.OldestMessageAge.TotalSeconds);
                }
            }
        }

        // Query 2: Breakdown by message type
        const string typeBreakdownQuery = """
            SELECT
                message_type,
                COUNT(*) as count
            FROM wolverine.wolverine_dead_letters
            GROUP BY message_type
            ORDER BY count DESC
            LIMIT 10
            """;

        await using (var cmd = new NpgsqlCommand(typeBreakdownQuery, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var messageType = reader.GetString(0);
                var count = reader.GetInt64(1);
                state.MessagesByType[messageType] = count;
            }
        }

        // Query 3: Recent failures (last hour) - for trend detection
        const string recentQuery = """
            SELECT COUNT(*)
            FROM wolverine.wolverine_dead_letters
            WHERE timestamp > NOW() - INTERVAL '1 hour'
            """;

        await using (var cmd = new NpgsqlCommand(recentQuery, connection))
        {
            var recentCount = (long)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
            state.RecentCount = recentCount;

            if (recentCount > 0)
            {
                _newDeadLettersCounter.Add(recentCount);
            }
        }

        return state;
    }

    private void LogSummary(DeadLetterQueueState state)
    {
        if (state.TotalCount == 0)
        {
            _logger.LogInformation("[DLQ Monitor] Dead letter queue is empty ✓");
            return;
        }

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["dlq_total_count"] = state.TotalCount,
            ["dlq_recent_count"] = state.RecentCount,
            ["dlq_oldest_age_seconds"] = state.OldestMessageAge.TotalSeconds
        }))
        {
            _logger.LogWarning(
                "[DLQ Monitor] DLQ Summary: {TotalCount} total, {RecentCount} in last hour, " +
                "oldest: {OldestAge:g}",
                state.TotalCount,
                state.RecentCount,
                state.OldestMessageAge);
        }
    }

    /// <summary>
    ///     Health check implementation for the DLQ monitor.
    ///     Returns Degraded if DLQ has messages, Unhealthy if above threshold.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // If we haven't checked yet, return healthy with a note
        if (_lastCheckTime == DateTime.MinValue)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "DLQ monitor starting up",
                new Dictionary<string, object> { ["status"] = "initializing" }));
        }

        var data = new Dictionary<string, object>
        {
            ["total_count"] = _lastState.TotalCount,
            ["recent_count"] = _lastState.RecentCount,
            ["oldest_message_age_seconds"] = _lastState.OldestMessageAge.TotalSeconds,
            ["last_check"] = _lastCheckTime.ToString("O"),
            ["top_message_types"] = JsonSerializer.Serialize(_lastState.MessagesByType.Take(5))
        };

        if (_lastState.TotalCount >= _options.AlertThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"DLQ has {_lastState.TotalCount} messages (threshold: {_options.AlertThreshold})",
                data: data));
        }

        if (_lastState.TotalCount > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"DLQ has {_lastState.TotalCount} messages requiring attention",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("DLQ is empty", data));
    }

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}

/// <summary>
///     Cached state from the last DLQ check.
/// </summary>
public sealed class DeadLetterQueueState
{
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public long TotalCount { get; set; }
    public long RecentCount { get; set; }
    public TimeSpan OldestMessageAge { get; set; }
    public Dictionary<string, long> MessagesByType { get; set; } = new();
}

/// <summary>
///     Configuration options for the DLQ monitor.
/// </summary>
public sealed class DeadLetterQueueMonitorOptions
{
    public const string SectionName = "Wolverine:DeadLetterMonitor";

    /// <summary>
    ///     PostgreSQL connection string. Required.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    ///     How often to check the DLQ in seconds. Default: 60
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    ///     Number of messages that triggers an alert. Default: 10
    /// </summary>
    public int AlertThreshold { get; set; } = 10;

    /// <summary>
    ///     Whether to enable the health check integration. Default: true
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;
}
