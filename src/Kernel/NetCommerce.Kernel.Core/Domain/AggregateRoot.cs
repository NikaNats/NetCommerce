#nullable enable
namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Base class for aggregate roots with optimistic concurrency support.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot<TId> where TId : notnull
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
