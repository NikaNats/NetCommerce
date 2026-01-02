using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Service for managing integration event log entries.
/// </summary>
public class IntegrationEventLogService<TContext> : IIntegrationEventLogService
    where TContext : IIntegrationEventLogDbContext
{
    private readonly TContext _context;

    public IntegrationEventLogService(TContext context)
    {
        _context = context;
    }

    /// <summary>
    ///     Marks an integration event as published in the audit log.
    /// </summary>
    public async Task MarkEventAsPublishedAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var eventLogEntry = await _context.IntegrationEventLogs
            .FirstOrDefaultAsync(ie => ie.EventId == eventId, cancellationToken);

        if (eventLogEntry != null && eventLogEntry.Status == IntegrationEventLogStatus.Pending)
        {
            eventLogEntry.MarkAsPublished();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    ///     Marks an integration event as in-progress (being published).
    /// </summary>
    public async Task MarkEventAsInProgressAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var eventLogEntry = await _context.IntegrationEventLogs
            .FirstOrDefaultAsync(ie => ie.EventId == eventId, cancellationToken);

        if (eventLogEntry != null && eventLogEntry.Status == IntegrationEventLogStatus.Pending)
        {
            eventLogEntry.MarkAsInProgress();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    ///     Marks an integration event as failed in the audit log.
    /// </summary>
    public async Task MarkEventAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
    {
        var eventLogEntry = await _context.IntegrationEventLogs
            .FirstOrDefaultAsync(ie => ie.EventId == eventId, cancellationToken);

        if (eventLogEntry != null)
        {
            eventLogEntry.MarkAsFailed(error);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
