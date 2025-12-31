namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
/// Configuration options for the Outbox Processor.
/// </summary>
public class OutboxProcessorOptions
{
    public const string SectionName = "OutboxProcessor";

    /// <summary>
    /// Interval between polling cycles in milliseconds. Default is 1000ms (1 second).
    /// </summary>
    public int PollingIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Number of messages to process per batch. Default is 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of retries for failed messages. Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Whether to enable the outbox processor. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout in seconds after which a message stuck in "Processing" state 
    /// will be considered abandoned and can be reclaimed by another worker.
    /// Default is 300 seconds (5 minutes).
    /// </summary>
    public int StuckMessageTimeoutSeconds { get; set; } = 300;
}
