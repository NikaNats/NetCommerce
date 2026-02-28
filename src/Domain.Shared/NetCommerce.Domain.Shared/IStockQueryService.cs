#nullable enable

namespace NetCommerce.Domain.Shared;

/// <summary>
///     Cross-module contract: Service interface for querying stock information.
///     Defined in Domain.Shared because it's consumed by Catalog and implemented by Inventory.
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
