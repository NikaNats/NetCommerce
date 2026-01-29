using Microsoft.EntityFrameworkCore;
using NetCommerce.Kernel.Application;
using NetCommerce.SharedKernel.Domain;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence;

/// <summary>
///     Base DbContext with Wolverine transactional outbox integration.
///     Domain events are captured in the Outbox and dispatched reliably.
///     This provides at-least-once delivery guarantees for cross-module messaging.
/// </summary>
public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    protected BaseDbContext(DbContextOptions options) : base(options)
    {
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Update audit fields
        UpdateAuditableEntities();

        // Save changes - Wolverine middleware handles domain event dispatch via outbox
        var result = await base.SaveChangesAsync(cancellationToken);

        return result;
    }

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

    private void UpdateAuditableEntities()
    {
        var entries = ChangeTracker.Entries<IAuditableEntity>();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = utcNow;

            if (entry.State == EntityState.Modified) entry.Entity.ModifiedAt = utcNow;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // CRITICAL: Map Wolverine's transactional outbox/inbox envelope storage
        // This creates wolverine_incoming_envelopes and wolverine_outgoing_envelopes tables
        modelBuilder.MapWolverineEnvelopeStorage();

        // Configure rowversion for all aggregate roots (optimistic concurrency)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            if (entityType.ClrType.IsAssignableTo(typeof(NetCommerce.Kernel.Core.Domain.AggregateRoot<>)))
                modelBuilder.Entity(entityType.ClrType)
                    .Property<uint>("Version")
                    .IsRowVersion();
    }
}

/// <summary>
///     Interface for auditable entities.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? ModifiedAt { get; set; }
}
