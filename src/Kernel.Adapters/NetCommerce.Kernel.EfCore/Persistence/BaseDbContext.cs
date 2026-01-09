#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Base DbContext with Tenant Isolation, Audit, and Domain Events.
/// </summary>
public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    // We inject the TenantContext to use it in Query Filters
    private readonly ITenantContext _tenantContext;

    protected BaseDbContext(
        DbContextOptions options,
        ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    ///     Exposes the current TenantId to the Expression Tree compiler.
    ///     This property is accessed by the Query Filter lambda.
    /// </summary>
    public string? CurrentTenantId => _tenantContext.TenantId;

    // ---------------------------------------------------------------
    // REMOVED: SaveChangesAsync override
    // REASON: Logic moved to AuditInterceptor & TenantSaveInterceptor
    // ---------------------------------------------------------------

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        await Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        await Database.CommitTransactionAsync(cancellationToken);
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        await Database.RollbackTransactionAsync(cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Register the convention
        configurationBuilder.Conventions.Add(_ => new StronglyTypedIdConvention());

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply Soft Delete & Multi-Tenancy Filters
        modelBuilder.ApplyKernelGlobalFilters(this);

        // CRITICAL: Map Wolverine's transactional outbox/inbox envelope storage
        // This creates wolverine_incoming_envelopes and wolverine_outgoing_envelopes tables
        // Note: This method may not be available in older versions of Wolverine.EntityFrameworkCore
        // modelBuilder.MapWolverineEnvelopeStorage();
    }
}
