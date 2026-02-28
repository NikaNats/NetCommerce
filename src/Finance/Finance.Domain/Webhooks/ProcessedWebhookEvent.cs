#nullable enable
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Finance.Domain.Webhooks;

/// <summary>
///     Entity tracking processed Stripe webhook events for idempotency.
///     Prevents duplicate processing when Stripe retries webhooks.
///
///     <para>
///     <b>Idempotency Strategy:</b>
///     - Store event ID before processing begins (claim the slot)
///     - If event ID already exists, skip processing and return 200 OK
///     - Stripe retries on 4xx/5xx for up to 72 hours
///     - We retain records for 7 days (configurable) then purge
///     </para>
///
///     <para>
///     <b>Why not Redis?</b>
///     - Financial audit trail requires durable storage
///     - PostgreSQL provides ACID guarantees
///     - Event records support reconciliation debugging
///     </para>
/// </summary>
public sealed class ProcessedWebhookEvent : Entity<Guid>
{
    private ProcessedWebhookEvent() { } // EF Core

    public string StripeEventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public DateTime ReceivedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public WebhookProcessingStatus Status { get; private set; }
    public string? PaymentIntentId { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>
    ///     Claims a webhook event ID for processing. Returns null if already claimed.
    /// </summary>
    public static ProcessedWebhookEvent Create(
        string stripeEventId,
        string eventType,
        string? paymentIntentId = null)
    {
        return new ProcessedWebhookEvent
        {
            Id = Guid.NewGuid(),
            StripeEventId = stripeEventId,
            EventType = eventType,
            PaymentIntentId = paymentIntentId,
            ReceivedAt = DateTime.UtcNow,
            Status = WebhookProcessingStatus.Processing
        };
    }

    public void MarkAsProcessed()
    {
        Status = WebhookProcessingStatus.Processed;
        ProcessedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string errorMessage)
    {
        Status = WebhookProcessingStatus.Failed;
        ProcessedAt = DateTime.UtcNow;
        ErrorMessage = errorMessage;
    }

    public void MarkAsSkipped(string reason)
    {
        Status = WebhookProcessingStatus.Skipped;
        ProcessedAt = DateTime.UtcNow;
        ErrorMessage = reason;
    }
}

public enum WebhookProcessingStatus
{
    Processing = 0,
    Processed = 1,
    Failed = 2,
    Skipped = 3 // e.g., unhandled event type
}
