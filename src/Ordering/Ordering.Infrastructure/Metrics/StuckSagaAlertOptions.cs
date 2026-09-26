#nullable enable
namespace NetCommerce.Ordering.Infrastructure.Metrics;

/// <summary>
///     Configuration for stuck-saga alerting.
///     Bind from the <c>Ordering:Alerting</c> section.
/// </summary>
public sealed class StuckSagaAlertOptions
{
    public const string SectionName = "Ordering:Alerting";

    /// <summary>
    ///     Master switch for the alert sweep.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     How often to sweep for stuck sagas, in seconds.
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 300;

    /// <summary>
    ///     PagerDuty Events API v2 routing key. When empty, the sweep still
    ///     emits CRITICAL logs (SIEM path) but pages nobody — fail visibly.
    /// </summary>
    public string? PagerDutyRoutingKey { get; set; }
}
