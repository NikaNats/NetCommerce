namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
///     Transactional Outbox entry for guaranteed event delivery.
///     Events are saved in the same transaction as aggregate changes,
///     then processed asynchronously by a background worker.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTime OccurredOn { get; private set; }
    public DateTime? ProcessedOn { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }

    /// <summary>
    ///     Current processing status. Used to prevent race conditions
    ///     when multiple workers process messages concurrently.
    /// </summary>
    public OutboxMessageStatus Status { get; private set; } = OutboxMessageStatus.Pending;

    /// <summary>
    ///     Timestamp when the message was claimed for processing.
    ///     Used for detecting stuck messages that can be reclaimed.
    /// </summary>
    public DateTime? ProcessingStartedAt { get; private set; }

    public static OutboxMessage Create(string type, string content, DateTime occurredOn, Guid eventId)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Type = type,
            Content = content,
            OccurredOn = occurredOn,
            Status = OutboxMessageStatus.Pending
        };
    }

    /// <summary>
    ///     Claims the message for processing. Called after SELECT FOR UPDATE SKIP LOCKED.
    /// </summary>
    public void ClaimForProcessing()
    {
        Status = OutboxMessageStatus.Processing;
        ProcessingStartedAt = DateTime.UtcNow;
    }

    public void MarkAsProcessed()
    {
        ProcessedOn = DateTime.UtcNow;
        Status = OutboxMessageStatus.Processed;
        Error = null;
    }

    public void MarkAsFailed(string error, int maxRetries)
    {
        Error = error;
        RetryCount++;

        // If max retries exceeded, mark as permanently failed
        // Otherwise, return to Pending so it can be retried
        Status = RetryCount >= maxRetries
            ? OutboxMessageStatus.Failed
            : OutboxMessageStatus.Pending;

        ProcessingStartedAt = null;
    }

    /// <summary>
    ///     Marks the message as failed without considering max retries (legacy overload).
    ///     The message will remain in Pending status for retry.
    /// </summary>
    public void MarkAsFailed(string error)
    {
        Error = error;
        RetryCount++;
        Status = OutboxMessageStatus.Pending;
        ProcessingStartedAt = null;
    }

    /// <summary>
    ///     Releases the claim on the message, returning it to Pending status.
    ///     Used when a worker crashes or times out during processing.
    /// </summary>
    public void ReleaseClaim()
    {
        if (Status == OutboxMessageStatus.Processing)
        {
            Status = OutboxMessageStatus.Pending;
            ProcessingStartedAt = null;
        }
    }

    public bool CanRetry(int maxRetries)
    {
        return RetryCount < maxRetries;
    }

    /// <summary>
    ///     Checks if the message is stuck in Processing state for too long.
    /// </summary>
    public bool IsStuck(TimeSpan timeout)
    {
        return Status == OutboxMessageStatus.Processing
               && ProcessingStartedAt.HasValue
               && DateTime.UtcNow - ProcessingStartedAt.Value > timeout;
    }
}