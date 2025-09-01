namespace Catalog.Domain.Common;

/// <summary>
/// Interface for domain events that occur within the domain layer.
/// Domain events represent something that happened in the business domain.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// The date and time when the event occurred.
    /// </summary>
    DateTime OccurredOn { get; }

    /// <summary>
    /// Unique identifier for the event.
    /// </summary>
    Guid EventId { get; }
}