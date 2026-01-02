using Microsoft.EntityFrameworkCore;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Repository implementation for querying integration event logs.
/// </summary>
public sealed class IntegrationEventLogRepository<TContext> : IIntegrationEventLogRepository<TContext>
    where TContext : IIntegrationEventLogDbContext
{
    private readonly TContext _dbContext;

    public IntegrationEventLogRepository(TContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<IntegrationEventLog>> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IntegrationEventLogs
            .Where(e => e.EventId == eventId)
            .OrderBy(e => e.LoggedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<IntegrationEventLog>> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IntegrationEventLogs
            .Where(e => e.CorrelationId == correlationId)
            .OrderBy(e => e.LoggedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<IntegrationEventLog>> GetByEventTypeAsync(string eventType, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IntegrationEventLogs
            .Where(e => e.EventType == eventType)
            .OrderByDescending(e => e.OccurredOn)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<IntegrationEventLog>> GetByDateRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.IntegrationEventLogs
            .Where(e => e.OccurredOn >= from && e.OccurredOn <= to)
            .OrderByDescending(e => e.OccurredOn)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<IntegrationEventLog>> GetByDirectionAndStatusAsync(
        IntegrationEventLogDirection direction,
        IntegrationEventLogStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.IntegrationEventLogs
            .Where(e => e.Direction == direction && e.Status == status)
            .OrderByDescending(e => e.OccurredOn)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<IntegrationEventLog>> GetByTraceIdAsync(string traceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.IntegrationEventLogs
            .Where(e => e.TraceId == traceId)
            .OrderBy(e => e.LoggedAt)
            .ToListAsync(cancellationToken);
    }

}