#nullable enable

namespace NetCommerce.Inventory.Application.Stock.Queries;

/// <summary>
///     Service interface for querying stock information.
///     Used by other modules (like Catalog) to get stock data without coupling to Inventory domain.
/// </summary>
public interface IStockQueryService
{
    /// <summary>
    ///     Gets the available stock quantity for a product.
    /// </summary>
    Task<int> GetAvailableQuantityAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets available stock quantities for multiple products at once.
    ///     More efficient than calling GetAvailableQuantityAsync multiple times.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetAvailableQuantitiesAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default);
}
