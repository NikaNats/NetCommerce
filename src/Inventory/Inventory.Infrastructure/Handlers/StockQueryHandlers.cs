using Microsoft.EntityFrameworkCore;
using NetCommerce.Inventory.Application.Stock.Mappers;
using NetCommerce.Inventory.Application.Stock.Queries;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Results;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Infrastructure.Handlers;

/// <summary>
///     Wolverine handler for GetStockByProductIdQuery.
/// </summary>
[WolverineHandler]
public static class GetStockByProductIdHandler
{
    public static async Task<Result<StockDto>> HandleAsync(
        GetStockByProductIdQuery query,
        InventoryDbContext db,
        IStockMapper mapper,
        CancellationToken cancellationToken)
    {
        var stock = await db.Stocks
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == query.ProductId, cancellationToken);

        if (stock is null)
            return Result.Failure<StockDto>(Error.NotFound("Stock", query.ProductId));

        return mapper.MapToDto(stock);
    }
}

/// <summary>
///     Wolverine handler for GetLowStockItemsQuery.
/// </summary>
[WolverineHandler]
public static class GetLowStockItemsHandler
{
    public static async Task<Result<IReadOnlyList<StockDto>>> HandleAsync(
        GetLowStockItemsQuery query,
        InventoryDbContext db,
        IStockMapper mapper,
        CancellationToken cancellationToken)
    {
        var stocks = await db.Stocks
            .AsNoTracking()
            .Where(s => s.Quantity <= s.LowStockThreshold)
            .ToListAsync(cancellationToken);
        
        return Result.Success(mapper.MapToDto(stocks));
    }
}

/// <summary>
///     Wolverine handler for GetStockBySkuQuery.
/// </summary>
[WolverineHandler]
public static class GetStockBySkuHandler
{
    public static async Task<Result<StockDto>> HandleAsync(
        GetStockBySkuQuery query,
        InventoryDbContext db,
        IStockMapper mapper,
        CancellationToken cancellationToken)
    {
        var stock = await db.Stocks
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Sku == query.Sku, cancellationToken);

        if (stock is null)
            return Result.Failure<StockDto>(
                Error.NotFound("Stock", $"sku:{query.Sku}"));

        return mapper.MapToDto(stock);
    }
}