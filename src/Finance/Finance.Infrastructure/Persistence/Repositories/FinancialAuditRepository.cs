#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Finance.Domain.Audit;

namespace NetCommerce.Finance.Infrastructure.Persistence.Repositories;

/// <summary>
///     Append-only repository for financial audit trail.
///     No Update/Delete operations are provided by design.
/// </summary>
public class FinancialAuditRepository : IFinancialAuditRepository
{
    private readonly FinanceDbContext _context;

    public FinancialAuditRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task AppendAsync(FinancialAuditEntry entry, CancellationToken ct = default)
    {
        _context.Set<FinancialAuditEntry>().Add(entry);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AppendRangeAsync(IEnumerable<FinancialAuditEntry> entries, CancellationToken ct = default)
    {
        _context.Set<FinancialAuditEntry>().AddRange(entries);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FinancialAuditEntry>> GetByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default)
    {
        return await _context.Set<FinancialAuditEntry>()
            .AsNoTracking()
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FinancialAuditEntry>> GetByDateRangeAsync(
        DateTime startUtc,
        DateTime endUtc,
        FinancialAuditType? filterByType = null,
        CancellationToken ct = default)
    {
        var query = _context.Set<FinancialAuditEntry>()
            .AsNoTracking()
            .Where(e => e.OccurredAt >= startUtc && e.OccurredAt <= endUtc);

        if (filterByType.HasValue)
        {
            query = query.Where(e => e.AuditType == filterByType.Value);
        }

        return await query
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FinancialAuditEntry>> GetByExternalTransactionAsync(
        string externalTransactionId,
        CancellationToken ct = default)
    {
        return await _context.Set<FinancialAuditEntry>()
            .AsNoTracking()
            .Where(e => e.ExternalTransactionId == externalTransactionId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FinancialAuditEntry>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken ct = default)
    {
        return await _context.Set<FinancialAuditEntry>()
            .AsNoTracking()
            .Where(e => e.CorrelationId == correlationId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync(ct);
    }
}
