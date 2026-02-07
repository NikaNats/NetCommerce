#nullable enable
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using Wolverine.EntityFrameworkCore;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     Consolidated Base DbContext with Tenant Isolation, Audit, Wolverine Outbox, and Domain Events.
///     This replaces the fragmented SharedKernel.Infrastructure.Persistence.BaseDbContext.
/// </summary>
[UnconditionalSuppressMessage("AOT", "IL2026:RequiresUnreferencedCode",
    Justification = "EF Core is not fully AOT compatible. DbContext base constructor requires dynamic code.")]
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "EF Core is not fully AOT compatible. DbContext base constructor requires dynamic code.")]
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
    // NOTE: SaveChangesAsync override removed
    // REASON: Audit logic moved to AuditInterceptor & TenantSaveInterceptor
    // Domain event dispatch handled by Wolverine middleware via transactional outbox
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
        // Register the convention for strongly-typed IDs
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
        // Enables reliable at-least-once delivery for cross-module integration events
        modelBuilder.MapWolverineEnvelopeStorage();

        // Configure rowversion for all aggregate roots (optimistic concurrency)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            if (entityType.ClrType.IsAssignableTo(typeof(AggregateRoot<>)))
                modelBuilder.Entity(entityType.ClrType)
                    .Property<uint>("Version")
                    .IsRowVersion();
    }
}
