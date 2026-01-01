namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
///     Status of an outbox message in the processing pipeline.
///     Used to prevent race conditions when multiple workers process messages concurrently.
/// </summary>
public enum OutboxMessageStatus
{
    /// <summary>
    ///     Message is waiting to be processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     Message has been claimed by a worker and is being processed.
    ///     Prevents other workers from picking up the same message.
    /// </summary>
    Processing = 1,

    /// <summary>
    ///     Message was successfully processed and the domain event was published.
    /// </summary>
    Processed = 2,

    /// <summary>
    ///     Message processing failed after exhausting all retries.
    /// </summary>
    Failed = 3
}