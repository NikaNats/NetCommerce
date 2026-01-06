using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Finance.Domain.Gateways;

/// <summary>
///     Gateway interface for Payment Service Provider reconciliation data.
///     Provides access to external financial truth for reconciliation.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    ///     Get all successful charges/transactions from PSP for a specific date.
    ///     This represents the "External Reality" for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ExternalTransaction>> GetExternalLedgerAsync(
        DateTime date,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Get detailed transaction information by external ID.
    /// </summary>
    Task<ExternalTransaction?> GetTransactionDetailsAsync(
        string externalTransactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Process a refund for a ghost charge or discrepancy resolution.
    /// </summary>
    Task<string> RefundTransactionAsync(
        string externalTransactionId,
        decimal amount,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     External transaction record from PSP.
///     Represents the "Source of Truth" for financial reconciliation.
/// </summary>
public record ExternalTransaction(
    string Id,
    decimal Amount,
    decimal Net, // Amount after PSP fees
    decimal Fee,
    string Currency,
    DateTime ProcessedAt,
    string? Description = null);
