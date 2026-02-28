#nullable enable

namespace NetCommerce.Finance.Domain.Webhooks;

/// <summary>
///     Repository interface for webhook event idempotency tracking.
/// </summary>
public interface IWebhookEventStore
{
    /// <summary>
    ///     Try to claim a webhook event for processing.
    ///     Returns true if this is a new event; false if already processed/processing.
    /// </summary>
    /// <remarks>
    ///     Uses PostgreSQL's ON CONFLICT DO NOTHING for atomic claim-or-skip.
    ///     This prevents race conditions when Stripe sends rapid retries.
    /// </remarks>
    Task<bool> TryClaimEventAsync(
        string stripeEventId,
        string eventType,
        string? paymentIntentId,
        CancellationToken ct = default);

    /// <summary>
    ///     Mark an event as successfully processed.
    /// </summary>
    Task MarkProcessedAsync(string stripeEventId, CancellationToken ct = default);

    /// <summary>
    ///     Mark an event as failed (will be retried by Stripe).
    /// </summary>
    Task MarkFailedAsync(string stripeEventId, string errorMessage, CancellationToken ct = default);

    /// <summary>
    ///     Check if an event has already been processed.
    /// </summary>
    Task<bool> IsEventProcessedAsync(string stripeEventId, CancellationToken ct = default);

    /// <summary>
    ///     Get processing status for debugging/audit.
    /// </summary>
    Task<ProcessedWebhookEvent?> GetEventAsync(string stripeEventId, CancellationToken ct = default);

    /// <summary>
    ///     Purge old events (retention policy enforcement).
    /// </summary>
    Task<int> PurgeOldEventsAsync(TimeSpan retentionPeriod, CancellationToken ct = default);
}
