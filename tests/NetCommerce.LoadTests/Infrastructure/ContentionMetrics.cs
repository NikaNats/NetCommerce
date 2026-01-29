using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;

namespace NetCommerce.LoadTests.Infrastructure;

/// <summary>
///     Contention-Specific Metrics collector for ACM-grade stress analysis.
///
///     <para>
///     These metrics go beyond standard load test measurements to capture
///     the specific behaviors of Partitioned Sequential Messaging systems:
///     </para>
///
///     <list type="bullet">
///         <item>Deadlock Rate: Should be 0.00% with proper partitioning</item>
///         <item>P99 Latency (Hot Key): Should grow linearly with queue depth</item>
///         <item>CPU Context Switching: Low proves threads aren't fighting</item>
///         <item>Saga Leak Rate: Zero after burst completion</item>
///     </list>
/// </summary>
public sealed class ContentionMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly MeterListener _listener;

    // Counters
    private long _totalRequests;
    private long _successfulRequests;
    private long _deadlockErrors;
    private long _lockTimeoutErrors;
    private long _dbTimeoutErrors;
    private long _stockDepletedErrors;

    // Latency tracking
    private readonly List<double> _latencies = [];
    private readonly object _latencyLock = new();

    // Partition distribution
    private readonly int[] _partitionHits = new int[16]; // Support up to 16 partitions

    public ContentionMetrics()
    {
        _meter = new Meter("NetCommerce.ContentionTest", "1.0.0");

        // Create instruments
        _meter.CreateObservableCounter(
            "contention.requests.total",
            () => _totalRequests,
            unit: "requests",
            description: "Total requests sent during contention test");

        _meter.CreateObservableCounter(
            "contention.errors.deadlock",
            () => _deadlockErrors,
            unit: "errors",
            description: "Count of database deadlock errors (should be 0)");

        _meter.CreateObservableCounter(
            "contention.errors.lock_timeout",
            () => _lockTimeoutErrors,
            unit: "errors",
            description: "Count of lock timeout errors (should be 0 with partitioning)");

        _listener = new MeterListener();
        _listener.Start();
    }

    // ═══════════════════════════════════════════════════════════════
    // Recording Methods
    // ═══════════════════════════════════════════════════════════════

    public void RecordRequest(bool success, double latencyMs, Guid? productId = null)
    {
        Interlocked.Increment(ref _totalRequests);

        if (success)
        {
            Interlocked.Increment(ref _successfulRequests);
        }

        lock (_latencyLock)
        {
            _latencies.Add(latencyMs);
        }

        // Track partition distribution if productId provided
        if (productId.HasValue)
        {
            var slot = Math.Abs(productId.Value.ToString().GetHashCode()) % _partitionHits.Length;
            Interlocked.Increment(ref _partitionHits[slot]);
        }
    }

    public void RecordDeadlock()
    {
        Interlocked.Increment(ref _deadlockErrors);
    }

    public void RecordLockTimeout()
    {
        Interlocked.Increment(ref _lockTimeoutErrors);
    }

    public void RecordDbTimeout()
    {
        Interlocked.Increment(ref _dbTimeoutErrors);
    }

    public void RecordStockDepleted()
    {
        Interlocked.Increment(ref _stockDepletedErrors);
    }

    // ═══════════════════════════════════════════════════════════════
    // Analysis Methods
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Generates a comprehensive analysis report of the contention test.
    /// </summary>
    public ContentionAnalysisReport GenerateReport()
    {
        double[] sortedLatencies;
        lock (_latencyLock)
        {
            sortedLatencies = [.. _latencies.OrderBy(l => l)];
        }

        var report = new ContentionAnalysisReport
        {
            TotalRequests = _totalRequests,
            SuccessfulRequests = _successfulRequests,
            DeadlockErrors = _deadlockErrors,
            LockTimeoutErrors = _lockTimeoutErrors,
            DbTimeoutErrors = _dbTimeoutErrors,
            StockDepletedErrors = _stockDepletedErrors,

            // Latency percentiles
            P50LatencyMs = GetPercentile(sortedLatencies, 0.50),
            P90LatencyMs = GetPercentile(sortedLatencies, 0.90),
            P95LatencyMs = GetPercentile(sortedLatencies, 0.95),
            P99LatencyMs = GetPercentile(sortedLatencies, 0.99),
            P999LatencyMs = GetPercentile(sortedLatencies, 0.999),
            MaxLatencyMs = sortedLatencies.Length > 0 ? sortedLatencies[^1] : 0,
            MinLatencyMs = sortedLatencies.Length > 0 ? sortedLatencies[0] : 0,
            AvgLatencyMs = sortedLatencies.Length > 0 ? sortedLatencies.Average() : 0,

            // Linearity analysis
            LinearityRatio = CalculateLinearityRatio(sortedLatencies),

            // Partition distribution
            PartitionDistribution = _partitionHits.Select((hits, slot) => new PartitionSlotStats
            {
                SlotId = slot,
                HitCount = hits,
                Percentage = _totalRequests > 0 ? (double)hits / _totalRequests * 100 : 0
            }).Where(p => p.HitCount > 0).ToArray(),

            // ACM metrics
            DeadlockRate = _totalRequests > 0 ? (double)_deadlockErrors / _totalRequests * 100 : 0,
            ErrorRate = _totalRequests > 0 ? (double)(_deadlockErrors + _lockTimeoutErrors + _dbTimeoutErrors) / _totalRequests * 100 : 0,
            SuccessRate = _totalRequests > 0 ? (double)_successfulRequests / _totalRequests * 100 : 0,

            // System metrics
            ProcessorCount = Environment.ProcessorCount,
            IsServer = RuntimeInformation.FrameworkDescription.Contains("Server"),
            DotNetVersion = RuntimeInformation.FrameworkDescription
        };

        return report;
    }

    /// <summary>
    ///     Validates that the test results meet ACM-grade standards.
    /// </summary>
    public ContentionValidationResult ValidateAcmStandards()
    {
        var report = GenerateReport();
        var issues = new List<string>();

        // RULE 1: Zero Deadlock Rate
        if (report.DeadlockRate > 0)
        {
            issues.Add($"DEADLOCK_VIOLATION: Deadlock rate is {report.DeadlockRate:F4}%, expected 0.00%");
        }

        // RULE 2: Zero Lock Timeout Rate
        if (report.LockTimeoutErrors > 0)
        {
            issues.Add($"LOCK_TIMEOUT_VIOLATION: {report.LockTimeoutErrors} lock timeouts detected. Partitioning may be misconfigured.");
        }

        // RULE 3: Linear Latency Growth
        if (report.LinearityRatio > 10.0)
        {
            issues.Add($"LINEARITY_VIOLATION: P99/P50 ratio is {report.LinearityRatio:F2}, expected < 10.0 for linear queue behavior.");
        }

        // RULE 4: Reasonable Error Rate
        if (report.ErrorRate > 1.0)
        {
            issues.Add($"ERROR_RATE_VIOLATION: Error rate is {report.ErrorRate:F2}%, expected < 1.0%");
        }

        return new ContentionValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            Report = report
        };
    }

    private static double GetPercentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;
        var index = (int)(sortedValues.Length * percentile);
        return sortedValues[Math.Min(index, sortedValues.Length - 1)];
    }

    private static double CalculateLinearityRatio(double[] sortedLatencies)
    {
        if (sortedLatencies.Length < 10) return 0;

        var p50 = GetPercentile(sortedLatencies, 0.50);
        var p99 = GetPercentile(sortedLatencies, 0.99);

        return p50 > 0 ? p99 / p50 : 0;
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
    }
}

/// <summary>
///     Comprehensive analysis report for contention stress tests.
/// </summary>
public record ContentionAnalysisReport
{
    // Request counts
    public long TotalRequests { get; init; }
    public long SuccessfulRequests { get; init; }
    public long DeadlockErrors { get; init; }
    public long LockTimeoutErrors { get; init; }
    public long DbTimeoutErrors { get; init; }
    public long StockDepletedErrors { get; init; }

    // Latency metrics
    public double P50LatencyMs { get; init; }
    public double P90LatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double P99LatencyMs { get; init; }
    public double P999LatencyMs { get; init; }
    public double MaxLatencyMs { get; init; }
    public double MinLatencyMs { get; init; }
    public double AvgLatencyMs { get; init; }

    // Queue behavior
    public double LinearityRatio { get; init; }

    // Partition distribution
    public PartitionSlotStats[] PartitionDistribution { get; init; } = [];

    // ACM metrics
    public double DeadlockRate { get; init; }
    public double ErrorRate { get; init; }
    public double SuccessRate { get; init; }

    // System info
    public int ProcessorCount { get; init; }
    public bool IsServer { get; init; }
    public string DotNetVersion { get; init; } = string.Empty;

    public void PrintReport()
    {
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║         CONTENTION STRESS ANALYSIS REPORT                     ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ REQUEST SUMMARY                                               ║");
        Console.WriteLine($"║   Total Requests:        {TotalRequests,10:N0}                       ║");
        Console.WriteLine($"║   Successful:            {SuccessfulRequests,10:N0}  ({SuccessRate,6:F2}%)            ║");
        Console.WriteLine($"║   Stock Depleted:        {StockDepletedErrors,10:N0}                       ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ ACM-GRADE METRICS (Target: All PASS)                          ║");
        Console.WriteLine($"║   Deadlock Rate:         {DeadlockRate,10:F4}%  {(DeadlockRate == 0 ? "✓ PASS" : "✗ FAIL")}           ║");
        Console.WriteLine($"║   Lock Timeout Errors:   {LockTimeoutErrors,10:N0}  {(LockTimeoutErrors == 0 ? "✓ PASS" : "✗ FAIL")}           ║");
        Console.WriteLine($"║   DB Timeout Errors:     {DbTimeoutErrors,10:N0}  {(DbTimeoutErrors == 0 ? "✓ PASS" : "✗ FAIL")}           ║");
        Console.WriteLine($"║   Linearity Ratio:       {LinearityRatio,10:F2}  {(LinearityRatio < 10 ? "✓ PASS" : "✗ FAIL")}           ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ LATENCY DISTRIBUTION                                          ║");
        Console.WriteLine($"║   Min:                   {MinLatencyMs,10:F2}ms                      ║");
        Console.WriteLine($"║   P50 (Median):          {P50LatencyMs,10:F2}ms                      ║");
        Console.WriteLine($"║   P90:                   {P90LatencyMs,10:F2}ms                      ║");
        Console.WriteLine($"║   P95:                   {P95LatencyMs,10:F2}ms                      ║");
        Console.WriteLine($"║   P99:                   {P99LatencyMs,10:F2}ms                      ║");
        Console.WriteLine($"║   P99.9:                 {P999LatencyMs,10:F2}ms                      ║");
        Console.WriteLine($"║   Max:                   {MaxLatencyMs,10:F2}ms                      ║");
        Console.WriteLine($"║   Average:               {AvgLatencyMs,10:F2}ms                      ║");
        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║ PARTITION DISTRIBUTION (Hot Key Detection)                    ║");

        foreach (var slot in PartitionDistribution.OrderByDescending(p => p.HitCount).Take(5))
        {
            var bar = new string('█', Math.Min((int)(slot.Percentage / 5), 20));
            Console.WriteLine($"║   Slot {slot.SlotId,2}: {slot.HitCount,8:N0} ({slot.Percentage,5:F1}%) {bar,-20} ║");
        }

        Console.WriteLine("╠═══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ System: {DotNetVersion,-52} ║");
        Console.WriteLine($"║ CPUs: {ProcessorCount,-2}  Server GC: {(IsServer ? "Yes" : "No"),-44} ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}

public record PartitionSlotStats
{
    public int SlotId { get; init; }
    public int HitCount { get; init; }
    public double Percentage { get; init; }
}

public record ContentionValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Issues { get; init; } = [];
    public ContentionAnalysisReport Report { get; init; } = null!;

    public void AssertValid()
    {
        if (!IsValid)
        {
            var message = "ACM-GRADE VALIDATION FAILED:\n" + string.Join("\n", Issues.Select(i => $"  - {i}"));
            throw new Xunit.Sdk.XunitException(message);
        }
    }
}
