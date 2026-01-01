using NetCommerce.Inventory.Application.Stock.Queries;

namespace NetCommerce.Inventory.Application.Stock.Mappers;

/// <summary>
///     Mapper for Stock domain entities to DTOs.
///     Centralizes mapping logic following DRY and Single Responsibility principles.
/// </summary>
public sealed class StockMapper : IStockMapper
{
    public StockDto MapToDto(Domain.Stock.Stock stock)
    {
        return new StockDto(
            stock.Id,
            stock.ProductId,
            stock.Sku,
            stock.Quantity,
            stock.ReservedQuantity,
            stock.AvailableQuantity,
            stock.LowStockThreshold,
            stock.IsLowStock,
            stock.LastUpdatedAt);
    }

    public IReadOnlyList<StockDto> MapToDto(IEnumerable<Domain.Stock.Stock> stocks)
    {
        return stocks.Select(MapToDto).ToList().AsReadOnly();
    }
}

/// <summary>
///     Interface for stock mapping operations.
///     Supports Dependency Inversion Principle.
/// </summary>
public interface IStockMapper
{
    StockDto MapToDto(Domain.Stock.Stock stock);
    IReadOnlyList<StockDto> MapToDto(IEnumerable<Domain.Stock.Stock> stocks);
}