namespace NetCommerce.Ordering.Infrastructure.BackgroundJobs;

/// <summary>
///     Configuration options for the grace period workflow.
///     Allows customization of timing parameters via appsettings.json.
/// </summary>
public sealed class GracePeriodOptions
{
    /// <summary>
    ///     Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "GracePeriod";

    /// <summary>
    ///     The duration in minutes that an order remains in the grace period.
    ///     During this time, customers can cancel without payment processing.
    ///     Default: 5 minutes.
    /// </summary>
    public int GracePeriodMinutes { get; set; } = 5;

    /// <summary>
    ///     The interval in seconds between grace period checks.
    ///     The background service will wake up every N seconds to process orders.
    ///     Default: 60 seconds (1 minute).
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    ///     Maximum number of orders to process in a single batch.
    ///     Prevents memory issues when processing large backlogs.
    ///     Default: 100 orders.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    ///     Whether the grace period manager is enabled.
    ///     Can be disabled in specific environments.
    ///     Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
