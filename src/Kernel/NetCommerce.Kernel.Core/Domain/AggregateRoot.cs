#nullable enable
namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Marker interface for aggregate roots.
/// </summary>
public interface IAggregateRoot : IHasDomainEvents
{
    uint Version { get; }
}

/// <summary>
///     Base class for aggregate roots with optimistic concurrency support.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : notnull
{
    protected AggregateRoot()
    {
    }

    protected AggregateRoot(TId id) : base(id)
    {
    }

    /// <summary>
    ///     Concurrency token for optimistic locking (RowVersion).
    /// </summary>
    public uint Version { get; protected set; }

    /// <summary>
    ///     Raises a domain event for this aggregate.
    /// </summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }
}
