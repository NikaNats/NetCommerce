#nullable enable
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using NetCommerce.Catalog.Domain.Products;

namespace NetCommerce.Catalog.Infrastructure.Persistence.Repositories;

/// <summary>
///     Decorator for IProductRepository that adds distributed caching via Redis.
///     Implements the Decorator pattern to transparently cache product reads.
///     Cache keys use product identifiers (ID, SKU, slug) for multi-level caching.
/// </summary>
public sealed class CachedProductRepository : IProductRepository
{
    private const string CacheKeyPrefix = "catalog:product";
    private const int CacheDurationSeconds = 3600; // 1 hour
    private readonly IDistributedCache _cache;
    private readonly IProductRepository _innerRepository;

    public CachedProductRepository(
        IProductRepository innerRepository,
        IDistributedCache cache)
    {
        _innerRepository = innerRepository ?? throw new ArgumentNullException(nameof(innerRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    ///     Gets a product by ID with caching.
    /// </summary>
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}:id:{id}";

        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached != null)
            try
            {
                return JsonSerializer.Deserialize<Product>(cached);
            }
            catch
            {
                // If deserialization fails, proceed to fetch from repository
                await _cache.RemoveAsync(cacheKey, cancellationToken);
            }

        var product = await _innerRepository.GetByIdAsync(id, cancellationToken);
        if (product != null) await CacheProductAsync(product, cancellationToken);

        return product;
    }

    /// <summary>
    ///     Gets a product by SKU with caching.
    ///     Caches both the product data and maintains a SKU→ID mapping.
    /// </summary>
    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var skuCacheKey = $"{CacheKeyPrefix}:sku:{sku}";

        var cached = await _cache.GetStringAsync(skuCacheKey, cancellationToken);
        if (cached != null)
            try
            {
                return JsonSerializer.Deserialize<Product>(cached);
            }
            catch
            {
                await _cache.RemoveAsync(skuCacheKey, cancellationToken);
            }

        var product = await _innerRepository.GetBySkuAsync(sku, cancellationToken);
        if (product != null) await CacheProductAsync(product, cancellationToken);

        return product;
    }

    /// <summary>
    ///     Gets a product by slug with caching.
    ///     Caches both the product data and maintains a slug→ID mapping.
    /// </summary>
    public async Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var slugCacheKey = $"{CacheKeyPrefix}:slug:{slug}";

        var cached = await _cache.GetStringAsync(slugCacheKey, cancellationToken);
        if (cached != null)
            try
            {
                return JsonSerializer.Deserialize<Product>(cached);
            }
            catch
            {
                await _cache.RemoveAsync(slugCacheKey, cancellationToken);
            }

        var product = await _innerRepository.GetBySlugAsync(slug, cancellationToken);
        if (product != null) await CacheProductAsync(product, cancellationToken);

        return product;
    }

    /// <summary>
    ///     Gets products by category.
    ///     Categories may have many products; caching the entire list for efficiency.
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}:category:{categoryId}";

        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached != null)
            try
            {
                return JsonSerializer.Deserialize<IReadOnlyList<Product>>(cached) ?? new List<Product>();
            }
            catch
            {
                await _cache.RemoveAsync(cacheKey, cancellationToken);
            }

        var products = await _innerRepository.GetByCategoryAsync(categoryId, cancellationToken);
        if (products.Count > 0)
        {
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheDurationSeconds)
            };

            var serialized = JsonSerializer.Serialize(products);
            await _cache.SetStringAsync(cacheKey, serialized, cacheOptions, cancellationToken);
        }

        return products;
    }

    /// <summary>
    ///     Full-text search.
    ///     Search results are cached per search term for common queries.
    /// </summary>
    public async Task<IReadOnlyList<Product>> SearchAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var normalizedTerm = string.IsNullOrWhiteSpace(searchTerm) ? "all" : searchTerm.ToLowerInvariant();
        var cacheKey = $"{CacheKeyPrefix}:search:{normalizedTerm}";

        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached != null)
            try
            {
                return JsonSerializer.Deserialize<IReadOnlyList<Product>>(cached) ?? new List<Product>();
            }
            catch
            {
                await _cache.RemoveAsync(cacheKey, cancellationToken);
            }

        var products = await _innerRepository.SearchAsync(searchTerm, cancellationToken);
        if (products.Count > 0)
        {
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheDurationSeconds)
            };

            var serialized = JsonSerializer.Serialize(products);
            await _cache.SetStringAsync(cacheKey, serialized, cacheOptions, cancellationToken);
        }

        return products;
    }

    /// <summary>
    ///     Checks if a product exists by SKU.
    ///     Does not cache the boolean result as it changes frequently.
    /// </summary>
    public async Task<bool> ExistsAsync(string sku, CancellationToken cancellationToken = default)
    {
        return await _innerRepository.ExistsAsync(sku, cancellationToken);
    }

    /// <summary>
    ///     Retrieves all products.
    ///     Generally not cached due to potential large result sets.
    ///     If performance is critical, consider paginating instead.
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _innerRepository.GetAllAsync(cancellationToken);
    }

    /// <summary>
    ///     Adds a new product (write operation).
    ///     Invalidates relevant caches.
    /// </summary>
    public async Task AddAsync(Product aggregate, CancellationToken cancellationToken = default)
    {
        await _innerRepository.AddAsync(aggregate, cancellationToken);

        // Cache the new product after insert
        await CacheProductAsync(aggregate, cancellationToken);
    }

    /// <summary>
    ///     Updates an existing product (write operation).
    ///     Invalidates all related caches.
    /// </summary>
    public void Update(Product aggregate)
    {
        _innerRepository.Update(aggregate);

        // Invalidate caches for this product
        // Note: We don't await here as this is a sync method.
        // In a real scenario, consider using a background job for cache invalidation.
        _ = InvalidateProductCachesAsync(aggregate);
    }

    /// <summary>
    ///     Removes a product (write operation).
    ///     Invalidates all related caches.
    /// </summary>
    public void Remove(Product aggregate)
    {
        _innerRepository.Remove(aggregate);

        // Invalidate caches for this product
        _ = InvalidateProductCachesAsync(aggregate);
    }

    /// <summary>
    ///     Caches a product using all its lookup keys (ID, SKU, slug).
    /// </summary>
    private async Task CacheProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(CacheDurationSeconds)
        };

        var serialized = JsonSerializer.Serialize(product);

        // Cache by ID
        var idKey = $"{CacheKeyPrefix}:id:{product.Id}";
        await _cache.SetStringAsync(idKey, serialized, cacheOptions, cancellationToken);

        // Cache by SKU
        var skuKey = $"{CacheKeyPrefix}:sku:{product.Sku}";
        await _cache.SetStringAsync(skuKey, serialized, cacheOptions, cancellationToken);

        // Cache by slug
        var slugKey = $"{CacheKeyPrefix}:slug:{product.Slug}";
        await _cache.SetStringAsync(slugKey, serialized, cacheOptions, cancellationToken);
    }

    /// <summary>
    ///     Invalidates all cache entries for a product.
    /// </summary>
    private async Task InvalidateProductCachesAsync(Product product)
    {
        var keys = new[]
        {
            $"{CacheKeyPrefix}:id:{product.Id}",
            $"{CacheKeyPrefix}:sku:{product.Sku}",
            $"{CacheKeyPrefix}:slug:{product.Slug}",
            $"{CacheKeyPrefix}:category:{product.CategoryId}"
        };

        foreach (var key in keys)
            try
            {
                await _cache.RemoveAsync(key);
            }
            catch
            {
                // Log and continue; cache removal failures should not block the application
            }
    }
}
