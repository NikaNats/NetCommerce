using System.Diagnostics;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Integration Event Log entry for auditability.
///     Records all integration events (both published and received) for compliance and debugging.
/// </summary>
public sealed class IntegrationEventLog
{
    private IntegrationEventLog()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>
    ///     The unique identifier of the integration event.
    /// </summary>
    public Guid EventId { get; private set; }

    /// <summary>
    ///     The type name of the integration event (e.g., "OrderSubmittedIntegrationEvent").
    /// </summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>
    ///     The serialized content of the integration event (JSON).
    /// </summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>
    ///     UTC timestamp when the event occurred (from the event itself).
    /// </summary>
    public DateTime OccurredOn { get; private set; }

    /// <summary>
    ///     UTC timestamp when the event was logged.
    /// </summary>
    public DateTime LoggedAt { get; private set; }

    /// <summary>
    ///     The direction of the event: Published (outgoing) or Received (incoming).
    /// </summary>
    public IntegrationEventLogDirection Direction { get; private set; }

    /// <summary>
    ///     Correlation ID for tracing the event across services/modules.
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    ///     OpenTelemetry TraceId for distributed tracing correlation.
    /// </summary>
    public string? TraceId { get; private set; }

    /// <summary>
    ///     OpenTelemetry SpanId for distributed tracing correlation.
    /// </summary>
    public string? SpanId { get; private set; }

    /// <summary>
    ///     The name of the handler that processed the event (if received).
    /// </summary>
    public string? HandlerName { get; private set; }

    /// <summary>
    ///     Processing status of the event.
    /// </summary>
    public IntegrationEventLogStatus Status { get; private set; }

    /// <summary>
    ///     Error message if processing failed.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    ///     UTC timestamp when the event was processed (if received).
    /// </summary>
    public DateTime? ProcessedAt { get; private set; }

    /// <summary>
    ///     Additional metadata stored as JSON.
    /// </summary>
    public string? Metadata { get; private set; }

    /// <summary>
    ///     Number of times this event has been sent/attempted.
    /// </summary>
    public int TimesSent { get; private set; }

    /// <summary>
    ///     Creates a log entry for a published integration event.
    ///     Automatically captures OpenTelemetry TraceId/SpanId from current Activity.
    /// </summary>
    public static IntegrationEventLog CreatePending(
        Guid eventId,
        string eventType,
        string content,
        DateTime occurredOn,
        string? correlationId = null,
        string? metadata = null)
    {
        var activity = Activity.Current;
        return new IntegrationEventLog
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventType = eventType,
            Content = content,
            OccurredOn = occurredOn,
            LoggedAt = DateTime.UtcNow,
            Direction = IntegrationEventLogDirection.Published,
            CorrelationId = correlationId ?? activity?.TraceId.ToString(),
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            Status = IntegrationEventLogStatus.Pending,
            Metadata = metadata
        };
    }

    /// <summary>
    ///     Creates a log entry for a received integration event.
    ///     Automatically captures OpenTelemetry TraceId/SpanId from current Activity.
    /// </summary>
    public static IntegrationEventLog CreateReceived(
        Guid eventId,
        string eventType,
        string content,
        DateTime occurredOn,
        string handlerName,
        string? correlationId = null,
        string? metadata = null)
    {
        var activity = Activity.Current;
        return new IntegrationEventLog
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventType = eventType,
            Content = content,
            OccurredOn = occurredOn,
            LoggedAt = DateTime.UtcNow,
            Direction = IntegrationEventLogDirection.Received,
            HandlerName = handlerName,
            CorrelationId = correlationId ?? activity?.TraceId.ToString(),
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            Status = IntegrationEventLogStatus.Pending,
            Metadata = metadata
        };
    }

    /// <summary>
    ///     Marks the event as successfully processed.
    /// </summary>
    public void MarkAsProcessed()
    {
        Status = IntegrationEventLogStatus.Processed;
        ProcessedAt = DateTime.UtcNow;
        Error = null;
    }

    /// <summary>
    ///     Marks the event as in-progress (being published).
    /// </summary>
    public void MarkAsInProgress()
    {
        Status = IntegrationEventLogStatus.Pending; // Still pending until confirmed
    }

    /// <summary>
    ///     Marks the event as failed during processing.
    /// </summary>
    public void MarkAsFailed(string error)
    {
        Status = IntegrationEventLogStatus.Failed;
        Error = error;
        TimesSent++;
        ProcessedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Marks the event as successfully published.
    /// </summary>
    public void MarkAsPublished()
    {
        Status = IntegrationEventLogStatus.Published;
        TimesSent++;
        ProcessedAt = DateTime.UtcNow;
        Error = null;
    }
}