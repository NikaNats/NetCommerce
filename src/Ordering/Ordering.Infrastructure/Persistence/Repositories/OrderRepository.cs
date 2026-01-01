using Microsoft.EntityFrameworkCore;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Ordering.Infrastructure.Persistence.Repositories;

public class OrderRepository : BaseRepository<Order, Guid>, IOrderRepository
{
    public OrderRepository(OrderingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);
    }

    public async Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByCustomerIdAsync(Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByStatusAsync(OrderStatus status,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(o => o.Items)
            .Where(o => o.Status == status)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public override async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default)
    {
        // Format: ORD-YYYYMMDD-XXXXX
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"ORD-{today}-";

        var lastOrder = await DbSet
            .Where(o => o.OrderNumber.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var sequence = 1;
        if (lastOrder != null)
        {
            var lastSequence = lastOrder.OrderNumber.Replace(prefix, "");
            if (int.TryParse(lastSequence, out var parsed)) sequence = parsed + 1;
        }

        return $"{prefix}{sequence:D5}";
    }
}