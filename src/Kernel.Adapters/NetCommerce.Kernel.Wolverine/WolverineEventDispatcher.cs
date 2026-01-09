#nullable enable
using NetCommerce.Kernel.Application;
using Wolverine;

namespace NetCommerce.Kernel.Wolverine;

/// <summary>
/// Wolverine implementation of the domain event dispatcher.
/// Bridges the Kernel.Application abstraction with Wolverine's IMessageBus.
/// </summary>
public sealed class WolverineEventDispatcher : IDomainEventDispatcher
{
    private readonly IMessageBus _bus;

    public WolverineEventDispatcher(IMessageBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public Task DispatchAsync(object domainEvent)
    {
        return _bus.PublishAsync(domainEvent).AsTask();
    }
}
