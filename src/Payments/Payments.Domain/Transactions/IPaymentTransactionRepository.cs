using NetCommerce.SharedKernel.Domain;

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
}