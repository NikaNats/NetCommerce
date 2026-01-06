#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Payments.Infrastructure.Persistence.Repositories;

public class PaymentTransactionRepository : BaseRepository<PaymentTransaction, Guid>, IPaymentTransactionRepository
{
    public PaymentTransactionRepository(PaymentsDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(pt => pt.OrderId == orderId)
            .OrderByDescending(pt => pt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentTransaction>> GetByOrderIdHistoryAsync(Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(pt => pt.OrderId == orderId)
            .OrderByDescending(pt => pt.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<PaymentTransaction?> GetByExternalIdAsync(string externalTransactionId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .FirstOrDefaultAsync(pt => pt.ExternalTransactionId == externalTransactionId, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentTransaction>> GetPendingPaymentsAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(pt => pt.Status == PaymentStatus.Pending && pt.CreatedAt < olderThan)
            .OrderBy(pt => pt.CreatedAt)
            .Take(100) // Limit to prevent overload
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentTransaction>> GetCompletedByDateAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        return await DbSet
            .Where(pt => pt.Status == PaymentStatus.Completed &&
                        pt.CompletedAt >= startOfDay &&
                        pt.CompletedAt < endOfDay)
            .OrderBy(pt => pt.CompletedAt)
            .ToListAsync(cancellationToken);
    }
}
