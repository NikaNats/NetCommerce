#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Compliance.Pii;

namespace NetCommerce.Finance.Infrastructure.Persistence.Repositories;

/// <summary>
///     EF Core implementation of the PII vault repository, backed by the
///     <c>finance.pii_vault_entries</c> table. Follows the
///     <see cref="FinancialAuditRepository"/> convention of saving directly:
///     vault writes are security-relevant and must not wait on an ambient unit
///     of work. Tenant isolation and soft-delete invisibility come from the
///     kernel global query filters, not from per-query predicates.
/// </summary>
public class PiiVaultRepository : IPiiVaultRepository<PiiVaultEntry>, ISearchablePiiVaultRepository<PiiVaultEntry>
{
    private readonly FinanceDbContext _context;

    public PiiVaultRepository(FinanceDbContext context)
    {
        _context = context;
    }

    public async Task<PiiVaultEntry?> FindByProfileIdAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.Set<PiiVaultEntry>()
            .FirstOrDefaultAsync(e => e.ProfileId == profileId, cancellationToken);

        if (entry is not null)
        {
            // The finance context defaults to NoTracking: attach before mutating,
            // otherwise RecordAccess is silently lost on SaveChanges.
            _context.Set<PiiVaultEntry>().Attach(entry);
            entry.RecordAccess();
            await _context.SaveChangesAsync(cancellationToken);
            _context.Entry(entry).State = EntityState.Detached;
        }

        return entry;
    }

    public async Task<List<PiiVaultEntry>> FindByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<PiiVaultEntry>()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PiiVaultEntry>> FindByBlindIndexAsync(
        string fieldName,
        string blindIndexValue,
        CancellationToken cancellationToken = default)
    {
        IQueryable<PiiVaultEntry> query = fieldName switch
        {
            nameof(PiiVaultEntry.EmailBlindIndex) => _context.Set<PiiVaultEntry>()
                .Where(e => e.EmailBlindIndex == blindIndexValue),
            nameof(PiiVaultEntry.PhoneBlindIndex) => _context.Set<PiiVaultEntry>()
                .Where(e => e.PhoneBlindIndex == blindIndexValue),
            _ => throw new ArgumentException(
                $"Unsupported blind index field '{fieldName}'. " +
                $"Supported: {nameof(PiiVaultEntry.EmailBlindIndex)}, {nameof(PiiVaultEntry.PhoneBlindIndex)}.",
                nameof(fieldName))
        };

        return await query.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(PiiVaultEntry entry, CancellationToken cancellationToken = default)
    {
        _context.Set<PiiVaultEntry>().Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(entry).State = EntityState.Detached;
    }

    public async Task UpdateAsync(PiiVaultEntry entry, CancellationToken cancellationToken = default)
    {
        _context.Set<PiiVaultEntry>().Update(entry);
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(entry).State = EntityState.Detached;
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.Set<PiiVaultEntry>()
            .FirstOrDefaultAsync(e => e.ProfileId == profileId, cancellationToken);

        if (entry is null)
            return;

        entry.MarkAsDeleted();
        // FinanceDbContext defaults to NoTracking: attach the mutation,
        // otherwise the GDPR erasure is silently lost on SaveChanges.
        _context.Set<PiiVaultEntry>().Update(entry);
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(entry).State = EntityState.Detached;
    }

    public async Task HardDeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.Set<PiiVaultEntry>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ProfileId == profileId, cancellationToken);

        if (entry is null)
            return;

        _context.Set<PiiVaultEntry>().Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<PiiVaultEntry>> GetEntriesForKeyRotationAsync(
        int currentKeyVersion,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<PiiVaultEntry>()
            .Where(e => e.KeyVersion < currentKeyVersion)
            .OrderBy(e => e.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }
}
