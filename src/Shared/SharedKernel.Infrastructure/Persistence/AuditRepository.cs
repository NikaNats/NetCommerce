#region

using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Persistence;

/// <summary>
///     2025 Elite Pattern: PostgreSQL implementation of the Immutable Audit Ledger.
///     Security Hardening (configured at DB level):
///     - GRANT INSERT, SELECT ON audit_logs TO app_user
///     - REVOKE UPDATE, DELETE ON audit_logs FROM app_user
///     - Even compromised admin users cannot tamper with history
///     Performance Optimizations:
///     - Partitioned by timestamp (monthly/yearly)
///     - Composite indexes on common query patterns
///     - Consider TimescaleDB for time-series workloads
/// </summary>
public class AuditRepository : IAuditRepository
{
    private readonly DbSet<AuditEntry> _auditLogs;
    private readonly DbContext _dbContext;

    public AuditRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
        _auditLogs = dbContext.Set<AuditEntry>();
    }

    /// <summary>
    ///     Stores an audit entry in the append-only ledger.
    ///     This is a fire-and-forget operation from the middleware perspective.
    /// </summary>
    public async Task StoreAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _auditLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    ///     Retrieves the complete audit timeline for a specific business entity.
    ///     Example: "Show me all actions on Order #12345"
    /// </summary>
    public async Task<IReadOnlyList<AuditEntry>> GetTimelineAsync(
        string resourceId,
        string? module = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AuditEntry> query = _auditLogs.Where(a => a.ResourceId == resourceId);

        if (!string.IsNullOrEmpty(module)) query = query.Where(a => a.Module == module);

        return await query
            .OrderBy(a => a.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Advanced query for compliance reports and investigations.
    ///     Example: "Show me all price changes by admin users in December 2025"
    /// </summary>
    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? userId = null,
        string? module = null,
        string? action = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AuditEntry> query = _auditLogs.AsQueryable();

        if (startDate.HasValue) query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue) query = query.Where(a => a.Timestamp <= endDate.Value);

        if (!string.IsNullOrEmpty(userId)) query = query.Where(a => a.UserId == userId);

        if (!string.IsNullOrEmpty(module)) query = query.Where(a => a.Module == module);

        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
