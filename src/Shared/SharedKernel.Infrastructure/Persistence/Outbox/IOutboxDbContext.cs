using Microsoft.EntityFrameworkCore;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
/// Interface for DbContexts that support the transactional outbox pattern.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
