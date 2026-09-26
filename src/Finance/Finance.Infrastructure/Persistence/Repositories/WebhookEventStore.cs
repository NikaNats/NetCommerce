#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Domain.Webhooks;

namespace NetCommerce.Finance.Infrastructure.Persistence.Repositories;

/// <summary>
///     WebhookEventStore implementation using PostgreSQL for durable idempotency.
///
///     <para>
    /// <b>Idempotency Pattern:</b>
    ///     Uses INSERT ... ON CONFLICT DO UPDATE (re-claim only when prior
    ///     status is Failed) for atomic claim-or-skip.
    ///     This ensures exactly-once processing even under rapid Stripe retries,
    ///     while allowing Stripe's 72-hour retry window to recover transient failures.
///     </para>
///
///     <para>
///     <b>AOT Compatibility:</b>
///     - Uses raw SQL with const strings (CA2100-safe)
///     - Parameterized queries via Npgsql
///     - No reflection or dynamic SQL
///     </para>
/// </summary>
public class WebhookEventStore : IWebhookEventStore
{
    private readonly FinanceDbContext _context;
    private readonly ILogger<WebhookEventStore> _logger;

    public WebhookEventStore(FinanceDbContext context, ILogger<WebhookEventStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> TryClaimEventAsync(
        string stripeEventId,
        string eventType,
        string? paymentIntentId,
        CancellationToken ct = default)
    {
        // Atomically insert when new, or re-claim when the previous attempt failed.
        // Without the DO UPDATE branch, a Stripe retry after a transient failure
        // would hit ON CONFLICT DO NOTHING, return 0 rows, and be misreported as
        // an already-processed duplicate (HTTP 200) — permanently dropping the
        // event while Stripe stops retrying.
        // Concurrency safety: under READ COMMITTED, concurrent reclaim attempts
        // serialize on the row lock and re-check the WHERE clause, so only one
        // claimant observes rowsAffected == 1.
        // NOTE: Identifiers are quoted PascalCase to match the EF Core model
        // (no snake_case naming convention is configured; see the Finance
        // migrations where columns are "Id", "StripeEventId", ...).
        const string sql = """
            INSERT INTO finance.processed_webhook_events
                ("Id", "StripeEventId", "EventType", "PaymentIntentId", "ReceivedAt", "Status", "ErrorMessage")
            VALUES
                ({0}, {1}, {2}, {3}, {4}, {5}, NULL)
            ON CONFLICT ("StripeEventId")
            DO UPDATE SET
                "Status" = {5},
                "ReceivedAt" = {4},
                "ErrorMessage" = NULL
            WHERE finance.processed_webhook_events."Status" = 'Failed'
            """;

        var id = Guid.NewGuid();
        var receivedAt = DateTime.UtcNow;
        var status = WebhookProcessingStatus.Processing.ToString();

        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
            sql, [id, stripeEventId, eventType, paymentIntentId ?? (object)DBNull.Value, receivedAt, status], ct);

        if (rowsAffected == 0)
        {
            _logger.LogInformation(
                "Webhook event {EventId} already claimed/processed, skipping duplicate",
                stripeEventId);
            return false;
        }

        _logger.LogDebug("Claimed webhook event {EventId} for processing", stripeEventId);
        return true;
    }

    public async Task MarkProcessedAsync(string stripeEventId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE finance.processed_webhook_events
            SET "Status" = {0}, "ProcessedAt" = {1}
            WHERE "StripeEventId" = {2}
            """;

        await _context.Database.ExecuteSqlRawAsync(
            sql,
            [WebhookProcessingStatus.Processed.ToString(), DateTime.UtcNow, stripeEventId],
            ct);

        _logger.LogDebug("Marked webhook event {EventId} as processed", stripeEventId);
    }

    public async Task MarkFailedAsync(string stripeEventId, string errorMessage, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE finance.processed_webhook_events
            SET "Status" = {0}, "ProcessedAt" = {1}, "ErrorMessage" = {2}
            WHERE "StripeEventId" = {3}
            """;

        await _context.Database.ExecuteSqlRawAsync(
            sql,
            [WebhookProcessingStatus.Failed.ToString(), DateTime.UtcNow, errorMessage, stripeEventId],
            ct);

        _logger.LogWarning("Marked webhook event {EventId} as failed: {Error}", stripeEventId, errorMessage);
    }

    public async Task<bool> IsEventProcessedAsync(string stripeEventId, CancellationToken ct = default)
    {
        return await _context.Set<ProcessedWebhookEvent>()
            .AnyAsync(e => e.StripeEventId == stripeEventId &&
                          e.Status == WebhookProcessingStatus.Processed, ct);
    }

    public async Task<ProcessedWebhookEvent?> GetEventAsync(string stripeEventId, CancellationToken ct = default)
    {
        return await _context.Set<ProcessedWebhookEvent>()
            .FirstOrDefaultAsync(e => e.StripeEventId == stripeEventId, ct);
    }

    public async Task<int> PurgeOldEventsAsync(TimeSpan retentionPeriod, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;

        const string sql = """
            DELETE FROM finance.processed_webhook_events
            WHERE "ReceivedAt" < {0}
            """;

        var deleted = await _context.Database.ExecuteSqlRawAsync(sql, [cutoff], ct);

        if (deleted > 0)
        {
            _logger.LogInformation("Purged {Count} old webhook events (older than {Cutoff})", deleted, cutoff);
        }

        return deleted;
    }
}
