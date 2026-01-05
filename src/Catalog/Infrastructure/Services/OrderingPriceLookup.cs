#region

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Ordering.Domain.Orders;

#endregion

namespace NetCommerce.Catalog.Infrastructure.Services;

public sealed class OrderingPriceLookup : IPriceLookupService
{
    private readonly IServiceProvider _serviceProvider;

    public OrderingPriceLookup(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<Dictionary<Guid, PriceSnapshot>> GetPricesAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        Guid[] requestedIds = productIds as Guid[] ?? productIds?.ToArray() ?? Array.Empty<Guid>();

        if (requestedIds.Length == 0)
            return new Dictionary<Guid, PriceSnapshot>();

        CatalogDbContext db = _serviceProvider.GetRequiredService<CatalogDbContext>();

        return await db.Products
            .AsNoTracking()
            .Where(p => requestedIds.Contains(p.Id))
            .ToDictionaryAsync(
                p => p.Id,
                p => new PriceSnapshot(p.Name, p.Price, p.Sku, p.WeightKg),
                cancellationToken);
    }
}
