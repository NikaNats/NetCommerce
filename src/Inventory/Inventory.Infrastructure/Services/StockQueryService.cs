#nullable enable

using Microsoft.EntityFrameworkCore;
using NetCommerce.Inventory.Application.Stock.Queries;
using NetCommerce.Inventory.Infrastructure.Persistence;

namespace NetCommerce.Inventory.Infrastructure.Services;

/// <summary>
///     Implementation of IStockQueryService.
///     Provides read-only stock queries for cross-module communication.
/// </summary>
public sealed class StockQueryService : IStockQueryService
{
    private readonly InventoryDbContext _context;

    public StockQueryService(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<int> GetAvailableQuantityAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var stock = await _context.Stocks
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

        return stock?.AvailableQuantity ?? 0;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetAvailableQuantitiesAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var productIdList = productIds.ToList();

        if (productIdList.Count == 0)
            return new Dictionary<Guid, int>();

        var stocks = await _context.Stocks
            .AsNoTracking()
            .Where(s => productIdList.Contains(s.ProductId))
            .Select(s => new { s.ProductId, s.AvailableQuantity })
            .ToListAsync(cancellationToken);

        // Create dictionary with all requested IDs, defaulting to 0 for missing
        var result = productIdList.ToDictionary(id => id, _ => 0);

        foreach (var stock in stocks)
        {
            result[stock.ProductId] = stock.AvailableQuantity;
        }

        return result;
    }
}
