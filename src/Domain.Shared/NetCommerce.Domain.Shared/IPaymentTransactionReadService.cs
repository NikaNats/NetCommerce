#nullable enable

namespace NetCommerce.Domain.Shared;

/// <summary>
///     Cross-module read-only contract for querying payment transaction data.
///     Used by the Finance module for reconciliation and webhook processing.
///     Implemented by the Payments module infrastructure layer.
/// </summary>
public interface IPaymentTransactionReadService
{
    /// <summary>
    ///     Find a payment transaction by its external (PSP) ID.
    /// </summary>
    Task<PaymentTransactionSummary?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Get completed payment transactions for a specific date.
    ///     Used by Financial Reconciliation System for comparing internal vs external ledgers.
    /// </summary>
    Task<IReadOnlyList<PaymentTransactionSummary>> GetCompletedByDateAsync(DateTime date, CancellationToken cancellationToken = default);
}

/// <summary>
///     Cross-module read model representing essential payment transaction data.
///     Avoids coupling Finance to the full PaymentTransaction aggregate.
/// </summary>
public record PaymentTransactionSummary(
    Guid Id,
    Guid OrderId,
    Money Amount,
    string? ExternalTransactionId);
