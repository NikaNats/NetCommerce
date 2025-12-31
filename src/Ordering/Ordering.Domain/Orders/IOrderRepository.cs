using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
/// Repository interface for Order aggregate.
/// </summary>
public interface IOrderRepository : IRepository<Order, Guid>
{
    Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByCustomerIdAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default);
}
