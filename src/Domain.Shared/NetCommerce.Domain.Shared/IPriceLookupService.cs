#nullable enable

namespace NetCommerce.Domain.Shared;

/// <summary>
///     Cross-module contract: Resolves the latest catalog metadata for the requested products.
///     Defined in Domain.Shared because it's consumed by Ordering and implemented by Catalog.
/// </summary>
public interface IPriceLookupService
{
    Task<Dictionary<Guid, PriceSnapshot>> GetPricesAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Snapshot of the catalog metadata that should be stored with the order item.
///     Includes physical weight for shipping label accuracy and category for tax calculation.
/// </summary>
public record PriceSnapshot(
    string Name,
    Money Price,
    string Sku,
    decimal WeightKg,
    string? Category = null);
