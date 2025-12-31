using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Base DbContext with domain events dispatching and audit support.
/// </summary>
public abstract class BaseDbContext : DbContext, IUnitOfWork
{
    private readonly IMediator _mediator;
    
    protected BaseDbContext(DbContextOptions options, IMediator mediator) : base(options)
    {
        _mediator = mediator;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Update audit fields
        UpdateAuditableEntities();
        
        // Get domain events before saving
        var domainEvents = GetDomainEvents();
        
        // Save changes
        var result = await base.SaveChangesAsync(cancellationToken);
        
        // Dispatch domain events after successful save
        await DispatchDomainEventsAsync(domainEvents, cancellationToken);
        
        return result;
    }

    private void UpdateAuditableEntities()
    {
        var entries = ChangeTracker.Entries<IAuditableEntity>();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
            }
            
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = utcNow;
            }
        }
    }

    private List<IDomainEvent> GetDomainEvents()
    {
        var domainEntities = ChangeTracker.Entries<Entity<Guid>>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(e => e.DomainEvents)
            .ToList();

        domainEntities.ForEach(e => e.ClearDomainEvents());

        return domainEvents;
    }

    private async Task DispatchDomainEventsAsync(
        List<IDomainEvent> domainEvents, 
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure rowversion for all aggregate roots
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IAggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<uint>("Version")
                    .IsRowVersion();
            }
        }
    }
}

/// <summary>
/// Interface for auditable entities.
/// </summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime? ModifiedAt { get; set; }
}
