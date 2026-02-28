#nullable enable

namespace NetCommerce.Finance.Domain.Audit;

/// <summary>
///     Repository for financial audit trail. Append-only by design.
/// </summary>
public interface IFinancialAuditRepository
{
    /// <summary>
    ///     Append an audit entry. This is the ONLY write operation allowed.
    /// </summary>
    Task AppendAsync(FinancialAuditEntry entry, CancellationToken ct = default);

    /// <summary>
    ///     Append multiple audit entries in a batch.
    /// </summary>
    Task AppendRangeAsync(IEnumerable<FinancialAuditEntry> entries, CancellationToken ct = default);

    /// <summary>
    ///     Query audit trail for an entity. For compliance/forensics.
    /// </summary>
    Task<IReadOnlyList<FinancialAuditEntry>> GetByEntityAsync(
        string entityType,
        string entityId,
        CancellationToken ct = default);

    /// <summary>
    ///     Query audit trail for a time range. For reconciliation/compliance.
    /// </summary>
    Task<IReadOnlyList<FinancialAuditEntry>> GetByDateRangeAsync(
        DateTime startUtc,
        DateTime endUtc,
        FinancialAuditType? filterByType = null,
        CancellationToken ct = default);

    /// <summary>
    ///     Query by external transaction ID (Stripe payment_intent).
    /// </summary>
    Task<IReadOnlyList<FinancialAuditEntry>> GetByExternalTransactionAsync(
        string externalTransactionId,
        CancellationToken ct = default);

    /// <summary>
    ///     Query by correlation ID (distributed tracing).
    /// </summary>
    Task<IReadOnlyList<FinancialAuditEntry>> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken ct = default);
}
