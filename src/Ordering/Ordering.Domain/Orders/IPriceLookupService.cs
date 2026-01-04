using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Resolves the latest catalog metadata for the requested products.
/// </summary>
public interface IPriceLookupService
{
    Task<Dictionary<Guid, PriceSnapshot>> GetPricesAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Snapshot of the catalog metadata that should be stored with the order item.
/// </summary>
public record PriceSnapshot(string Name, Money Price, string Sku);
