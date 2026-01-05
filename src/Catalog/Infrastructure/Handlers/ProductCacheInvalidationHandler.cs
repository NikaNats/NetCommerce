using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Infrastructure.Handlers;

/// <summary>
///     Outbox-backed cache invalidation for catalog products.
/// </summary>
public static class ProductCacheInvalidationHandler
{
    // Runs after the DB transaction commits via Wolverine's outbox.
    [WolverineHandler]
    public static async Task Handle(
        ProductUpdatedDomainEvent @event,
        HybridCache cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invalidating cache for product {ProductId} via tags", @event.ProductId);

        // Invalidate by ID
        await cache.RemoveByTagAsync($"product-{@event.ProductId}", cancellationToken);

        // Invalidate by SKU (Old and New)
        if (!string.IsNullOrWhiteSpace(@event.OldSku))
        {
            await cache.RemoveByTagAsync($"product-sku-{@event.OldSku}", cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(@event.NewSku))
        {
            await cache.RemoveByTagAsync($"product-sku-{@event.NewSku}", cancellationToken);
        }

        // Invalidate by Slug (Old and New)
        if (!string.IsNullOrWhiteSpace(@event.OldSlug))
        {
            await cache.RemoveByTagAsync($"product-slug-{@event.OldSlug}", cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(@event.NewSlug))
        {
            await cache.RemoveByTagAsync($"product-slug-{@event.NewSlug}", cancellationToken);
        }
    }

    // Price changes are emitted as a dedicated domain event.
    [WolverineHandler]
    public static async Task Handle(
        ProductPriceChangedDomainEvent @event,
        HybridCache cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invalidating cache for product {ProductId} price change", @event.ProductId);

        await cache.RemoveByTagAsync($"product-{@event.ProductId}", cancellationToken);

        if (!string.IsNullOrWhiteSpace(@event.Sku))
        {
            await cache.RemoveByTagAsync($"product-sku-{@event.Sku}", cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(@event.Slug))
        {
            await cache.RemoveByTagAsync($"product-slug-{@event.Slug}", cancellationToken);
        }
    }
}
