using Microsoft.EntityFrameworkCore;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Repository interface for querying integration event logs.
/// </summary>
public interface IIntegrationEventLogRepository<TContext>
    where TContext : IIntegrationEventLogDbContext
{
    /// <summary>
    ///     Gets all integration event logs for a specific event ID.
    /// </summary>
    Task<List<IntegrationEventLog>> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all integration event logs for a specific correlation ID.
    /// </summary>
    Task<List<IntegrationEventLog>> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all integration event logs for a specific event type.
    /// </summary>
    Task<List<IntegrationEventLog>> GetByEventTypeAsync(string eventType, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets integration event logs within a date range.
    /// </summary>
    Task<List<IntegrationEventLog>> GetByDateRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets integration event logs by direction and status.
    /// </summary>
    Task<List<IntegrationEventLog>> GetByDirectionAndStatusAsync(
        IntegrationEventLogDirection direction,
        IntegrationEventLogStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all integration event logs for a specific OpenTelemetry TraceId.
    /// </summary>
    Task<List<IntegrationEventLog>> GetByTraceIdAsync(string traceId, CancellationToken cancellationToken = default);

}