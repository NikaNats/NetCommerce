using NetCommerce.Kernel.Application;

namespace NetCommerce.Payments.Domain.Transactions;

/// <summary>
///     Repository interface for PaymentTransaction aggregate.
/// </summary>
public interface IPaymentTransactionRepository : IRepository<PaymentTransaction, Guid>
{
    Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<PaymentTransaction?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentTransaction>> GetByOrderIdHistoryAsync(Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending payments older than the specified time.
    /// Used by PaymentReconciliationJob to catch missed/delayed webhooks.
    /// </summary>
    Task<IReadOnlyList<PaymentTransaction>> GetPendingPaymentsAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get completed payment transactions for a specific date.
    /// Used by Financial Reconciliation System for comparing internal vs external ledgers.
    /// </summary>
    Task<IReadOnlyList<PaymentTransaction>> GetCompletedByDateAsync(
        DateTime date,
        CancellationToken cancellationToken = default);
}
