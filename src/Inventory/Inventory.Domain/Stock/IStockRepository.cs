using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Inventory.Domain.Stock;

/// <summary>
/// Repository interface for Stock aggregate.
/// </summary>
public interface IStockRepository : IRepository<Stock, Guid>
{
    Task<Stock?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<Stock?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Stock>> GetLowStockItemsAsync(CancellationToken cancellationToken = default);
    Task<StockReservation?> GetReservationByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
