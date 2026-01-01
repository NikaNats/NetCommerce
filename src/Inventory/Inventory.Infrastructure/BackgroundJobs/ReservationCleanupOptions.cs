namespace NetCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
///     Configuration options for the Reservation Cleanup Job.
/// </summary>
public class ReservationCleanupOptions
{
    public const string SectionName = "ReservationCleanup";

    /// <summary>
    ///     Interval between cleanup cycles in milliseconds. Default is 60000ms (1 minute).
    /// </summary>
    public int IntervalMs { get; set; } = 60_000;

    /// <summary>
    ///     Number of expired reservations to process per batch. Default is 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    ///     Whether the cleanup job is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}