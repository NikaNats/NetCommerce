using Microsoft.EntityFrameworkCore;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Interface for DbContext that supports integration event logging.
/// </summary>
public interface IIntegrationEventLogDbContext
{
    DbSet<IntegrationEventLog> IntegrationEventLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}