using NetCommerce.Kernel.Application;

namespace NetCommerce.Inventory.Domain.Stock;

/// <summary>
///     Repository interface for Stock aggregate.
/// </summary>
public interface IStockRepository : IRepository<Stock, Guid>
{
    Task<Stock?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets stock by product ID with a pessimistic lock (FOR UPDATE).
    ///     Use this when modifying stock to prevent concurrent updates.
    /// </summary>
    Task<Stock?> GetByProductIdForUpdateAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<Stock?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Stock>> GetLowStockItemsAsync(CancellationToken cancellationToken = default);
    Task<StockReservation?> GetReservationByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
