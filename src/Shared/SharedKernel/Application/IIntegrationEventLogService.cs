namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     Service for managing integration event log entries.
/// </summary>
public interface IIntegrationEventLogService
{
    /// <summary>
    ///     Marks an integration event as published in the audit log.
    /// </summary>
    Task MarkEventAsPublishedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks an integration event as in-progress (being published).
    /// </summary>
    Task MarkEventAsInProgressAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks an integration event as failed in the audit log.
    /// </summary>
    Task MarkEventAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default);
}