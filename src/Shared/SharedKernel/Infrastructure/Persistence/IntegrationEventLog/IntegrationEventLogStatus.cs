namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Processing status of the integration event.
/// </summary>
public enum IntegrationEventLogStatus
{
    /// <summary>
    ///     The event is committed to the database and waiting to be published by the Outbox.
    ///     (Used for Outgoing/Published events)
    /// </summary>
    Pending = 1, // Renamed from "Logged" or "Published"

    /// <summary>
    ///     The event was successfully handled by the consumer.
    ///     (Used for Incoming/Received events via Decorator)
    /// </summary>
    Processed = 2,

    /// <summary>
    ///     Processing failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    ///     (Optional) Updates from the Outbox Processor if you choose to double-write.
    /// </summary>
    Published = 4
}