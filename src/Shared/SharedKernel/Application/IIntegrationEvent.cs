using MediatR;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Application;

/// <summary>
/// Integration event marker interface.
/// Integration events are used for cross-module communication.
/// </summary>
public interface IIntegrationEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
    string EventType { get; }
}

/// <summary>
/// Base implementation for integration events.
/// </summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    public string EventType => GetType().Name;
}

/// <summary>
/// Handler for integration events.
/// </summary>
public interface IIntegrationEventHandler<TEvent> : INotificationHandler<TEvent>
    where TEvent : IIntegrationEvent;
