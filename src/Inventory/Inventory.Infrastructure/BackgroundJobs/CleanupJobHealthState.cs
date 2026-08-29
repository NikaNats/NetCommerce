#nullable enable
namespace NetCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Shared singleton state for ReservationCleanupJob health.
/// K8s readiness probe fails when IsDegraded is true, removing pod from LB rotation.
/// </summary>
public sealed class CleanupJobHealthState
{
    public bool IsDegraded { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
}
