using Catalog.Domain.Common;

namespace Catalog.Domain.Events;

/// <summary>
/// Base class for domain events with common properties.
/// </summary>
public abstract class DomainEvent : IDomainEvent
{
    public DateTime OccurredOn { get; }
    public Guid EventId { get; }

    protected DomainEvent()
    {
        EventId = Guid.NewGuid();
        OccurredOn = DateTime.UtcNow;
    }
}