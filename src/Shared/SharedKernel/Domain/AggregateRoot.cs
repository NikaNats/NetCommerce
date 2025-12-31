namespace NetCommerce.SharedKernel.Domain;

/// <summary>
/// Base class for aggregate roots with optimistic concurrency support.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : notnull
{
    /// <summary>
    /// Concurrency token for optimistic locking (RowVersion).
    /// </summary>
    public uint Version { get; protected set; }

    protected AggregateRoot() { }

    protected AggregateRoot(TId id) : base(id) { }

    /// <summary>
    /// Raises a domain event for this aggregate.
    /// </summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }
}

/// <summary>
/// Marker interface for aggregate roots.
/// </summary>
public interface IAggregateRoot
{
    uint Version { get; }
}
