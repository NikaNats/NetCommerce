#nullable enable
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Finance.Domain.Audit;

/// <summary>
///     Immutable audit log entry for financial state changes.
///     Every financial mutation (payment, refund, reconciliation) creates an audit entry.
///
///     <para>
///     <b>Design Principles:</b>
///     - Immutable: No setters, no updates allowed
///     - Append-only: INSERT only, no UPDATE/DELETE
///     - Complete: Captures before/after state for forensics
///     - Timestamped: Server-side UTC timestamp (not client provided)
///     </para>
///
///     <para>
///     <b>Compliance:</b>
///     - SOX: Financial transaction traceability
///     - PCI-DSS: Payment data access logging
///     - GDPR: Data processing records
///     </para>
/// </summary>
public sealed class FinancialAuditEntry : Entity<Guid>
{
    private FinancialAuditEntry() { } // EF Core

    /// <summary>
    ///     Type of financial operation being audited.
    /// </summary>
    public FinancialAuditType AuditType { get; private init; }

    /// <summary>
    ///     Entity being audited (Order, Payment, Refund, etc.)
    /// </summary>
    public string EntityType { get; private init; } = null!;

    /// <summary>
    ///     ID of the entity being audited.
    /// </summary>
    public string EntityId { get; private init; } = null!;

    /// <summary>
    ///     External transaction ID (Stripe payment_intent, etc.)
    /// </summary>
    public string? ExternalTransactionId { get; private init; }

    /// <summary>
    ///     Monetary amount involved (if applicable).
    /// </summary>
    public decimal? Amount { get; private init; }

    /// <summary>
    ///     Currency code (GEL, USD, EUR).
    /// </summary>
    public string? Currency { get; private init; }

    /// <summary>
    ///     Previous state (JSON serialized) for forensics.
    /// </summary>
    public string? PreviousState { get; private init; }

    /// <summary>
    ///     New state (JSON serialized) for forensics.
    /// </summary>
    public string? NewState { get; private init; }

    /// <summary>
    ///     User/system that initiated the action.
    /// </summary>
    public string ActorId { get; private init; } = null!;

    /// <summary>
    ///     Type of actor (User, System, Webhook, Scheduler).
    /// </summary>
    public ActorType ActorType { get; private init; }

    /// <summary>
    ///     Human-readable description of the action.
    /// </summary>
    public string Description { get; private init; } = null!;

    /// <summary>
    ///     Additional metadata (IP, User-Agent, Correlation ID, etc.)
    /// </summary>
    public string? Metadata { get; private init; }

    /// <summary>
    ///     Server-side timestamp (UTC).
    /// </summary>
    public DateTime OccurredAt { get; private init; }

    /// <summary>
    ///     Correlation ID for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; private init; }

    /// <summary>
    ///     Creates an immutable audit entry. All values set at creation, never modified.
    /// </summary>
    public static FinancialAuditEntry Create(
        FinancialAuditType auditType,
        string entityType,
        string entityId,
        string actorId,
        ActorType actorType,
        string description,
        string? externalTransactionId = null,
        decimal? amount = null,
        string? currency = null,
        string? previousState = null,
        string? newState = null,
        string? metadata = null,
        string? correlationId = null)
    {
        return new FinancialAuditEntry
        {
            Id = Guid.NewGuid(),
            AuditType = auditType,
            EntityType = entityType,
            EntityId = entityId,
            ExternalTransactionId = externalTransactionId,
            Amount = amount,
            Currency = currency,
            PreviousState = previousState,
            NewState = newState,
            ActorId = actorId,
            ActorType = actorType,
            Description = description,
            Metadata = metadata,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = correlationId
        };
    }
}

public enum FinancialAuditType
{
    PaymentInitiated = 0,
    PaymentSucceeded = 1,
    PaymentFailed = 2,
    PaymentCaptured = 3,
    RefundInitiated = 10,
    RefundSucceeded = 11,
    RefundFailed = 12,
    PartialRefund = 13,
    DisputeCreated = 20,
    DisputeUpdated = 21,
    DisputeWon = 22,
    DisputeLost = 23,
    ReconciliationStarted = 30,
    ReconciliationCompleted = 31,
    ReconciliationFailed = 32,
    DiscrepancyDetected = 33,
    DiscrepancyResolved = 34,
    ManualAdjustment = 40,
    GhostChargeDetected = 50,
    WebhookReceived = 60,
    WebhookProcessed = 61,
    AlertTriggered = 70
}

public enum ActorType
{
    User = 0,
    System = 1,
    Webhook = 2,
    Scheduler = 3,
    Admin = 4
}
