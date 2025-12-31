using NetCommerce.SharedKernel.Application;

namespace NetCommerce.Inventory.Application.Stock.Queries;

/// <summary>
/// DTO for stock information.
/// </summary>
public record StockDto(
    Guid Id,
    Guid ProductId,
    string Sku,
    int Quantity,
    int ReservedQuantity,
    int AvailableQuantity,
    int LowStockThreshold,
    bool IsLowStock,
    DateTime LastUpdatedAt);

/// <summary>
/// Query to get stock by product ID.
/// </summary>
public record GetStockByProductIdQuery(Guid ProductId) : IQuery<StockDto>;

/// <summary>
/// Query to get low stock items.
/// </summary>
public record GetLowStockItemsQuery : IQuery<IReadOnlyList<StockDto>>;

/// <summary>
/// Query to get stock by SKU.
/// </summary>
public record GetStockBySkuQuery(string Sku) : IQuery<StockDto>;
