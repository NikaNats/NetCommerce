#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Compliance.Audit;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     PostgreSQL implementation of the Immutable Audit Ledger.
///     Security Hardening (configured at DB level):
///     - GRANT INSERT, SELECT ON audit_logs TO app_user
///     - REVOKE UPDATE, DELETE ON audit_logs FROM app_user
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

    public async Task StoreAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _auditLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetTimelineAsync(
        string resourceId,
        string? module = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AuditEntry> query = _auditLogs.Where(a => a.ResourceId == resourceId);

        if (!string.IsNullOrEmpty(module))
            query = query.Where(a => a.Module == module);

        return await query
            .OrderBy(a => a.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

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

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(a => a.UserId == userId);

        if (!string.IsNullOrEmpty(module))
            query = query.Where(a => a.Module == module);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
