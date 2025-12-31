using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Base DbContext with transactional outbox pattern for guaranteed event delivery.
/// Domain events are converted to OutboxMessage entities and saved in the same transaction.
/// </summary>
public abstract class BaseDbContext : DbContext, IUnitOfWork, IOutboxDbContext
{
    private readonly IMediator _mediator;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected BaseDbContext(DbContextOptions options, IMediator mediator) : base(options)
    {
        _mediator = mediator;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Update audit fields
        UpdateAuditableEntities();
        
        // Convert domain events to outbox messages (within same transaction)
        ConvertDomainEventsToOutboxMessages();
        
        // Save changes (including outbox messages)
        var result = await base.SaveChangesAsync(cancellationToken);
        
        return result;
    }

    /// <summary>
    /// Converts domain events from tracked entities to OutboxMessage entries.
    /// These will be saved in the same transaction as the entity changes.
    /// </summary>
    private void ConvertDomainEventsToOutboxMessages()
    {
        var domainEntities = ChangeTracker.Entries<Entity<Guid>>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(e => e.DomainEvents)
            .ToList();

        foreach (var domainEvent in domainEvents)
        {
            var outboxMessage = OutboxMessage.Create(
                type: domainEvent.GetType().AssemblyQualifiedName!,
                content: JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions),
                occurredOn: domainEvent.OccurredOn
            );
            
            OutboxMessages.Add(outboxMessage);
        }

        domainEntities.ForEach(e => e.ClearDomainEvents());
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

        // Apply OutboxMessage configuration
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        
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
