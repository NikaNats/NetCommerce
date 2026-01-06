#nullable enable
namespace NetCommerce.Kernel.Compliance.Audit;

/// <summary>
///     Repository for the Immutable Business Event Store.
///     Security Model:
///     - Application user has INSERT and SELECT permissions ONLY
///     - NO UPDATE or DELETE permissions (enforced at DB level)
///     - This creates a "WORM" (Write Once, Read Many) ledger
/// </summary>
public interface IAuditRepository
{
    /// <summary>
    ///     Stores an audit entry in the immutable ledger.
    ///     This is typically called by audit middleware automatically.
    /// </summary>
    Task StoreAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Retrieves audit timeline for a specific business entity.
    ///     Example: Get all audit entries for Order #12345
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> GetTimelineAsync(
        string resourceId,
        string? module = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Advanced query for compliance reports.
    ///     Example: "Show me all price changes by Admin users in Q4 2025"
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? userId = null,
        string? module = null,
        string? action = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
