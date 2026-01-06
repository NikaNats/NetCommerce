using Microsoft.EntityFrameworkCore;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Infrastructure.Persistence.Configurations;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Finance.Infrastructure.Persistence;

/// <summary>
///     DbContext for Finance module with schema isolation.
///     Uses "finance" schema to maintain separation from operational modules.
///     This context is READ-ONLY for cross-module queries (e.g., Payments).
///     Finance acts as an Internal Auditor and should never modify operational data.
/// </summary>
public class FinanceDbContext : BaseDbContext
{
    public const string Schema = "finance";

    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
    {
    }

    public DbSet<ReconciliationSession> ReconciliationSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema isolation: All Finance tables live in "finance" schema
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new ReconciliationSessionConfiguration());
    }
}
