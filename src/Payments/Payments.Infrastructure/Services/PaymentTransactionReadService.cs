#nullable enable

using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Persistence;

namespace NetCommerce.Payments.Infrastructure.Services;

/// <summary>
///     Implementation of the cross-module read service for payment transactions.
///     Provides read-only access to payment data for Finance module reconciliation.
/// </summary>
public sealed class PaymentTransactionReadService : IPaymentTransactionReadService
{
    private readonly PaymentsDbContext _context;

    public PaymentTransactionReadService(PaymentsDbContext context)
    {
        _context = context;
    }

    public async Task<PaymentTransactionSummary?> GetByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<PaymentTransaction>()
            .AsNoTracking()
            .Where(pt => pt.ExternalTransactionId == externalId)
            .Select(pt => new PaymentTransactionSummary(
                pt.Id,
                pt.OrderId,
                pt.Amount,
                pt.ExternalTransactionId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentTransactionSummary>> GetCompletedByDateAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        return await _context.Set<PaymentTransaction>()
            .AsNoTracking()
            .Where(pt => pt.Status == PaymentStatus.Completed &&
                         pt.CompletedAt >= startOfDay &&
                         pt.CompletedAt < endOfDay)
            .OrderBy(pt => pt.CompletedAt)
            .Select(pt => new PaymentTransactionSummary(
                pt.Id,
                pt.OrderId,
                pt.Amount,
                pt.ExternalTransactionId))
            .ToListAsync(cancellationToken);
    }
}
