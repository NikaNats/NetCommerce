using Microsoft.EntityFrameworkCore;
using NetCommerce.Finance.Domain.Audit;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Domain.Webhooks;
using NetCommerce.Finance.Infrastructure.Persistence.Configurations;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Pii;
using NetCommerce.Kernel.EfCore.Persistence;

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

    public FinanceDbContext(DbContextOptions<FinanceDbContext> options, ITenantContext tenantContext) : base(options, tenantContext)
    {
    }

    // Best Practice: Use expression body for DbSets
    public DbSet<ReconciliationSession> ReconciliationSessions => Set<ReconciliationSession>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<FinancialAuditEntry> FinancialAuditLog => Set<FinancialAuditEntry>();
    public DbSet<PiiVaultEntry> PiiVaultEntries => Set<PiiVaultEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Default to NoTracking for performance (Finance is primarily read-only)
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema isolation: All Finance tables live in "finance" schema
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new ReconciliationSessionConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedWebhookEventConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new PiiVaultEntryConfiguration());
    }
}
