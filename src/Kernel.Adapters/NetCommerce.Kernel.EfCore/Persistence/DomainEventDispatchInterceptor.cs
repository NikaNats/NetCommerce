#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetCommerce.Kernel.Core.Domain;
using Wolverine;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     EF Core interceptor for automatic domain event dispatching via Wolverine.
///     Publishes domain events after SaveChanges completes successfully.
/// </summary>
public sealed class DomainEventDispatchInterceptor : SaveChangesInterceptor
{
    private readonly IMessageBus _messageBus;

    public DomainEventDispatchInterceptor(IMessageBus messageBus)
    {
        _messageBus = messageBus;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            await DispatchDomainEventsAsync(eventData.Context, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null)
        {
            DispatchDomainEventsAsync(eventData.Context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        return base.SavedChanges(eventData, result);
    }

    private async Task DispatchDomainEventsAsync(DbContext context, CancellationToken cancellationToken)
    {
        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .ToList();

        // Clear events before publishing to prevent re-publishing
        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        // Publish all domain events via Wolverine
        foreach (var domainEvent in domainEvents)
        {
            await _messageBus.PublishAsync(domainEvent);
        }
    }
}
