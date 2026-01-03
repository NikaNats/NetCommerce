namespace NetCommerce.SharedKernel.Domain;

/// <summary>
///     Marker interface for domain events.
///     Domain events are dispatched after aggregate changes are persisted.
///     Wolverine handles these via its transactional outbox pattern.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    ///     Unique identifier for this event instance.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    ///     UTC timestamp when the event occurred.
    /// </summary>
    DateTime OccurredOn { get; }
}

/// <summary>
///     Base implementation for domain events.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
