#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Kernel.EfCore.Persistence;

/// <summary>
///     EF Core interceptor for automatic domain event dispatching.
///     Publishes domain events after SaveChanges completes successfully.
///     Uses abstraction to avoid direct dependency on messaging infrastructure.
/// </summary>
public sealed class DomainEventDispatchInterceptor : SaveChangesInterceptor
{
    private readonly IDomainEventDispatcher _dispatcher;

    public DomainEventDispatchInterceptor(IDomainEventDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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
        // Microsoft Internal Guideline: Prevent synchronous IO in async-first infrastructures
        throw new InvalidOperationException("Use SaveChangesAsync when using the Domain Event Interceptor to prevent deadlocks.");
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

        // Publish all domain events via the abstracted dispatcher
        foreach (var domainEvent in domainEvents)
        {
            await _dispatcher.DispatchAsync(domainEvent);
        }
    }
}
