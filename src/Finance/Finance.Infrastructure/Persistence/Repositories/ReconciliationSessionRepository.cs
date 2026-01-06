#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Finance.Infrastructure.Persistence.Repositories;

public class ReconciliationSessionRepository : BaseRepository<ReconciliationSession, Guid>, IReconciliationSessionRepository
{
    public ReconciliationSessionRepository(FinanceDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<ReconciliationSession?> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == date.Date, cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationSession>> GetSessionsInDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Discrepancies)
            .Where(s => s.CalculatedForDate >= startDate.Date && s.CalculatedForDate <= endDate.Date)
            .OrderByDescending(s => s.CalculatedForDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationSession>> GetMismatchedSessionsAsync(
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Discrepancies)
            .Where(s => s.Status == ReconciliationStatus.Mismatched && s.StartedAt >= since)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(cancellationToken);
    }
}
