using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Infrastructure.Handlers;

/// <summary>
///     Outbox-backed cache invalidation for catalog products.
/// </summary>
public static class ProductCacheInvalidationHandler
{
    private const string CacheKeyPrefix = "catalog:product";

    // Runs after the DB transaction commits via Wolverine's outbox.
    [WolverineHandler]
    public static async Task Handle(
        ProductUpdatedDomainEvent @event,
        IDistributedCache cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{CacheKeyPrefix}:id:{@event.ProductId}"
        };

        AddCacheKey(keys, @event.OldSku, static sku => $"{CacheKeyPrefix}:sku:{sku}");
        AddCacheKey(keys, @event.NewSku, static sku => $"{CacheKeyPrefix}:sku:{sku}");
        AddCacheKey(keys, @event.OldSlug, static slug => $"{CacheKeyPrefix}:slug:{slug}");
        AddCacheKey(keys, @event.NewSlug, static slug => $"{CacheKeyPrefix}:slug:{slug}");

        foreach (var key in keys)
        {
            logger.LogInformation("Invalidating cache for {CacheKey}", key);
            await cache.RemoveAsync(key, cancellationToken);
        }
    }

    // Price changes are emitted as a dedicated domain event.
    [WolverineHandler]
    public static async Task Handle(
        ProductPriceChangedDomainEvent @event,
        IDistributedCache cache,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{CacheKeyPrefix}:id:{@event.ProductId}"
        };

        AddCacheKey(keys, @event.Sku, static sku => $"{CacheKeyPrefix}:sku:{sku}");
        AddCacheKey(keys, @event.Slug, static slug => $"{CacheKeyPrefix}:slug:{slug}");

        foreach (var key in keys)
        {
            logger.LogInformation("Invalidating cache for {CacheKey}", key);
            await cache.RemoveAsync(key, cancellationToken);
        }
    }

    private static void AddCacheKey(HashSet<string> keys, string? value, Func<string, string> formatter)
    {
        if (!string.IsNullOrWhiteSpace(value))
            keys.Add(formatter(value));
    }
}
