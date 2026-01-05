#nullable enable
using Microsoft.Extensions.Caching.Hybrid;
using NetCommerce.Catalog.Domain.Products;

namespace NetCommerce.Catalog.Infrastructure.Persistence.Repositories;

/// <summary>
///     Decorator for IProductRepository that adds distributed caching via HybridCache (L1+L2).
///     Implements the Decorator pattern to transparently cache product reads.
///     Uses HybridCache for stampede protection, tag-based invalidation, and multi-tier caching.
/// </summary>
public sealed class CachedProductRepository(
    IProductRepository innerRepository,
    HybridCache cache) : IProductRepository
{
    private const string CacheKeyPrefix = "catalog:product";

    /// <summary>
    ///     Gets a product by ID with caching.
    ///     Uses HybridCache to prevent stampedes and enable L1/L2 caching.
    /// </summary>
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}:id:{id}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async token => await innerRepository.GetByIdAsync(id, token),
            new HybridCacheEntryOptions
            {

                Expiration = TimeSpan.FromHours(1),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            },
            tags: ["catalog", $"product-{id}"],
            cancellationToken: cancellationToken
        );
    }

    /// <summary>
    ///     Gets a product by SKU with caching.
    /// </summary>
    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}:sku:{sku}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async token => await innerRepository.GetBySkuAsync(sku, token),
            new HybridCacheEntryOptions
            {

                Expiration = TimeSpan.FromHours(1),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            },
            tags: ["catalog", $"product-sku-{sku}"],
            cancellationToken: cancellationToken
        );
    }

    /// <summary>
    ///     Gets a product by slug with caching.
    /// </summary>
    public async Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}:slug:{slug}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async token => await innerRepository.GetBySlugAsync(slug, token),
            new HybridCacheEntryOptions
            {

                Expiration = TimeSpan.FromHours(1),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            },
            tags: ["catalog", $"product-slug-{slug}"],
            cancellationToken: cancellationToken
        );
    }

    /// <summary>
    ///     Gets products by category.
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}:category:{categoryId}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async token => await innerRepository.GetByCategoryAsync(categoryId, token),
            new HybridCacheEntryOptions
            {

                Expiration = TimeSpan.FromMinutes(30),
                LocalCacheExpiration = TimeSpan.FromMinutes(2)
            },
            tags: ["catalog", $"category-{categoryId}"],
            cancellationToken: cancellationToken
        ) ?? new List<Product>();
    }

    /// <summary>
    ///     Full-text search.
    /// </summary>
    public async Task<IReadOnlyList<Product>> SearchAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var normalizedTerm = string.IsNullOrWhiteSpace(searchTerm) ? "all" : searchTerm.ToLowerInvariant();
        var cacheKey = $"{CacheKeyPrefix}:search:{normalizedTerm}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async token => await innerRepository.SearchAsync(searchTerm, token),
            new HybridCacheEntryOptions
            {

                Expiration = TimeSpan.FromMinutes(15),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            tags: ["catalog", "search"],
            cancellationToken: cancellationToken
        ) ?? new List<Product>();
    }

    /// <summary>
    ///     Checks if a product exists by SKU.
    /// </summary>
    public async Task<bool> ExistsAsync(string sku, CancellationToken cancellationToken = default)
    {
        return await innerRepository.ExistsAsync(sku, cancellationToken);
    }

    /// <summary>
    ///     Retrieves all products.
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await innerRepository.GetAllAsync(cancellationToken);
    }

    /// <summary>
    ///     Adds a new product (write operation).
    /// </summary>
    public async Task AddAsync(Product aggregate, CancellationToken cancellationToken = default)
    {
        await innerRepository.AddAsync(aggregate, cancellationToken);
    }

    /// <summary>
    ///     Updates an existing product (write operation).
    /// </summary>
    public void Update(Product aggregate)
    {
        innerRepository.Update(aggregate);
    }

    /// <summary>
    ///     Removes a product (write operation).
    /// </summary>
    public void Remove(Product aggregate)
    {
        innerRepository.Remove(aggregate);
    }
}
