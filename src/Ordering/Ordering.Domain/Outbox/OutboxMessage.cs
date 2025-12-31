namespace NetCommerce.Ordering.Domain.Outbox;

/// <summary>
/// Transactional Outbox entry for guaranteed event delivery.
/// Events are saved in the same transaction as aggregate changes,
/// then processed asynchronously by a background worker.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public DateTime OccurredOn { get; private set; }
    public DateTime? ProcessedOn { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(string type, string content)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            Content = content,
            OccurredOn = DateTime.UtcNow
        };
    }

    public void MarkAsProcessed()
    {
        ProcessedOn = DateTime.UtcNow;
    }

    public void MarkAsFailed(string error)
    {
        Error = error;
        RetryCount++;
    }
}
