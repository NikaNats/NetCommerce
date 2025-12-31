using Microsoft.EntityFrameworkCore;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Payments.Infrastructure.Persistence.Repositories;

public class PaymentTransactionRepository : BaseRepository<PaymentTransaction, Guid>, IPaymentTransactionRepository
{
    public PaymentTransactionRepository(PaymentsDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(pt => pt.OrderId == orderId)
            .OrderByDescending(pt => pt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentTransaction>> GetByOrderIdHistoryAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(pt => pt.OrderId == orderId)
            .OrderByDescending(pt => pt.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<PaymentTransaction?> GetByExternalIdAsync(string externalTransactionId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(pt => pt.ExternalTransactionId == externalTransactionId, cancellationToken);
    }
}

