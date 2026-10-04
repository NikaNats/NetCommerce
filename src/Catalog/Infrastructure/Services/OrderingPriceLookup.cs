#region

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Domain.Shared;

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

        // The category NAME is part of this contract (Ordering taxes by
        // category via ITaxProvider), so translate it here at the boundary:
        // a null Category silently disables every category tax rule.
        // Left join: a product whose category row is gone still prices.
        return await (from p in db.Products.AsNoTracking()
                      join c in db.Categories.AsNoTracking()
                          on p.CategoryId equals c.Id into categories
                      from c in categories.DefaultIfEmpty()
                      where requestedIds.Contains(p.Id)
                      select new
                      {
                          p.Id,
                          Snapshot = new PriceSnapshot(
                              p.Name,
                              p.Price,
                              p.Sku,
                              p.WeightKg,
                              c != null ? c.Name : null)
                      })
            .ToDictionaryAsync(
                x => x.Id,
                x => x.Snapshot,
                cancellationToken);
    }
}
