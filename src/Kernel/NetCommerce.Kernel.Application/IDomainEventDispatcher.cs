#nullable enable

namespace NetCommerce.Kernel.Application;

/// <summary>
/// Abstraction for domain event dispatching to decouple infrastructure concerns.
/// Follows Dependency Inversion Principle - Kernel.Application doesn't depend on Wolverine.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches a domain event asynchronously.
    /// </summary>
    /// <param name="domainEvent">The domain event to dispatch.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DispatchAsync(object domainEvent);
}
