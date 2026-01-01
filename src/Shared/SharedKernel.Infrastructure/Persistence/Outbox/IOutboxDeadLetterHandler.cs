using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
///     Optional hook for handling messages that permanently fail processing (dead-letter).
///     This is invoked when an outbox message transitions to Failed after exhausting retries.
/// </summary>
public interface IOutboxDeadLetterHandler<in TDbContext>
    where TDbContext : DbContext, IOutboxDbContext
{
    Task HandleAsync(
        OutboxMessage message,
        IDomainEvent? domainEvent,
        Exception exception,
        CancellationToken cancellationToken);
}