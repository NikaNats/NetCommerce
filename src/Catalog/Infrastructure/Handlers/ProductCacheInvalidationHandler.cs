using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Infrastructure.Handlers;

/// <summary>
///     Outbox-backed cache invalidation for catalog products.
/// </summary>
/// <remarks>
/// DDIA derived-data contract: the HybridCache copies (by id/sku/slug,
/// by category, and the DB-backed <c>SearchAsync</c> listing) are derived from
/// the products table. Every domain event that changes a VISIBLE read shape
/// must evict the affected tags; staleness is then bounded by outbox delay,
/// not by TTL. Creation needs no handler: new products start as Draft, which
/// no cached read shape includes (listings filter Published).
/// </remarks>
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

        // Name/description edits change SearchAsync matches; the cached listing
        // carries the "search" tag.
        await cache.RemoveByTagAsync("search", cancellationToken);
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

        // Price edits change price-filtered SearchAsync matches.
        await cache.RemoveByTagAsync("search", cancellationToken);
    }

    // Visibility transitions change every listing shape (search, category).
    // Previously unhandled: a publish stayed invisible (and an archive stayed
    // listed) in cached copies for up to 30m/15m.
    [WolverineHandler]
    public static async Task Handle(
        ProductPublishedDomainEvent @event,
        HybridCache cache,
        ProductRepository products,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invalidating cache for product {ProductId} publish", @event.ProductId);

        await cache.RemoveByTagAsync($"product-{@event.ProductId}", cancellationToken);
        await cache.RemoveByTagAsync("search", cancellationToken);
        await EvictCategoryAsync(@event.ProductId, cache, products, logger, cancellationToken);
    }

    [WolverineHandler]
    public static async Task Handle(
        ProductArchivedDomainEvent @event,
        HybridCache cache,
        ProductRepository products,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invalidating cache for product {ProductId} archive", @event.ProductId);

        await cache.RemoveByTagAsync($"product-{@event.ProductId}", cancellationToken);
        await cache.RemoveByTagAsync("search", cancellationToken);
        await EvictCategoryAsync(@event.ProductId, cache, products, logger, cancellationToken);
    }

    private static async Task EvictCategoryAsync(
        Guid productId,
        HybridCache cache,
        ProductRepository products,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // The event carries no CategoryId, so resolve it fresh (inner repository,
        // bypassing the cache) and evict that category's listing. One DB read on
        // a rare admin lifecycle op bounds category-listing staleness to the
        // outbox delay instead of the 30m TTL.
        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            logger.LogWarning(
                "Cache invalidation: product {ProductId} no longer exists; skipping category eviction",
                productId);
            return;
        }

        await cache.RemoveByTagAsync($"category-{product.CategoryId}", cancellationToken);
    }
}
