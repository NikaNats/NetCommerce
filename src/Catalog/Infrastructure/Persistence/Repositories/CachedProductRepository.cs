#nullable enable
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using NetCommerce.Catalog.Domain.Products;

namespace NetCommerce.Catalog.Infrastructure.Persistence.Repositories;

/// <summary>
///     Decorator for IProductRepository that adds distributed caching via Redis.
///     Implements the Decorator pattern to transparently cache product reads.
///     Cache keys use product identifiers (ID, SKU, slug) for multi-level caching.
///
///     SECURITY: Implements "Shielded Negative Caching" to prevent Cache Penetration attacks.
///     Non-existent products are cached with a sentinel value to protect the database from
///     phantom lookups (e.g., bot attacks requesting thousands of fake product IDs).
/// </summary>
public sealed class CachedProductRepository : IProductRepository
{
    private const string CacheKeyPrefix = "catalog:product";
    private const int CacheDurationSeconds = 3600; // 1 hour for existing products
    private const string NotFoundSentinel = "SENTINEL_NOT_FOUND";
    private const int NegativeCacheDurationSeconds = 300; // 5 minutes for non-existent items

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
    ///     SECURITY: Implements negative caching to shield DB from Cache Penetration attacks.
    ///     Non-existent product IDs are cached for 5 minutes to prevent repeated DB queries.
    /// </summary>
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}:id:{id}";

        // 1. Try to get from cache
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            // 2. Check if it's a cached "Not Found" result (SHIELD)
            if (cached == NotFoundSentinel)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<Product>(cached);
            }
            catch
            {
                // If deserialization fails, proceed to fetch from repository
                await _cache.RemoveAsync(cacheKey, cancellationToken);
            }
        }

        // 3. Fallback to DB
        var product = await _innerRepository.GetByIdAsync(id, cancellationToken);

        if (product == null)
        {
            // 4. CRITICAL: Cache the absence to prevent DB DoS from repeated phantom lookups
            var negativeOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(NegativeCacheDurationSeconds)
            };
            await _cache.SetStringAsync(cacheKey, NotFoundSentinel, negativeOptions, cancellationToken);
            return null;
        }

        // 5. Normal caching for successful result
        await CacheProductAsync(product, cancellationToken);
        return product;
    }

    /// <summary>
    ///     Gets a product by SKU with caching.
    ///     Caches both the product data and maintains a SKU→ID mapping.
    ///     SECURITY: Implements negative caching to shield DB from SKU-based phantom lookups.
    /// </summary>
    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var skuCacheKey = $"{CacheKeyPrefix}:sku:{sku}";

        // 1. Try to get from cache
        var cached = await _cache.GetStringAsync(skuCacheKey, cancellationToken);
        if (cached != null)
        {
            // 2. Check for negative cache (SHIELD)
            if (cached == NotFoundSentinel)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<Product>(cached);
            }
            catch
            {
                await _cache.RemoveAsync(skuCacheKey, cancellationToken);
            }
        }

        // 3. Fallback to DB
        var product = await _innerRepository.GetBySkuAsync(sku, cancellationToken);

        if (product == null)
        {
            // 4. Shield DB from repeated SKU lookups (e.g., inventory scanner bots)
            var negativeOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(NegativeCacheDurationSeconds)
            };
            await _cache.SetStringAsync(skuCacheKey, NotFoundSentinel, negativeOptions, cancellationToken);
            return null;
        }

        // 5. Cache the successful result
        await CacheProductAsync(product, cancellationToken);
        return product;
    }

    /// <summary>
    ///     Gets a product by slug with caching.
    ///     Caches both the product data and maintains a slug→ID mapping.
    ///     SECURITY: Implements negative caching to shield DB from slug-based phantom lookups
    ///     (common in SEO crawlers and brute-force URL scanning).
    /// </summary>
    public async Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var slugCacheKey = $"{CacheKeyPrefix}:slug:{slug}";

        // 1. Try to get from cache
        var cached = await _cache.GetStringAsync(slugCacheKey, cancellationToken);
        if (cached != null)
        {
            // 2. Check for negative cache (SHIELD against URL scanning)
            if (cached == NotFoundSentinel)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<Product>(cached);
            }
            catch
            {
                await _cache.RemoveAsync(slugCacheKey, cancellationToken);
            }
        }

        // 3. Fallback to DB
        var product = await _innerRepository.GetBySlugAsync(slug, cancellationToken);

        if (product == null)
        {
            // 4. Shield DB from crawler/bot URL scans generating thousands of 404s
            var negativeOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(NegativeCacheDurationSeconds)
            };
            await _cache.SetStringAsync(slugCacheKey, NotFoundSentinel, negativeOptions, cancellationToken);
            return null;
        }

        // 5. Cache the successful result
        await CacheProductAsync(product, cancellationToken);
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
    ///     Cache population deferred until first read to avoid phantom cache entries.
    /// </summary>
    public async Task AddAsync(Product aggregate, CancellationToken cancellationToken = default)
    {
        await _innerRepository.AddAsync(aggregate, cancellationToken);
    }

    /// <summary>
    ///     Updates an existing product (write operation).
    ///     Cache invalidation is handled via domain events and Wolverine's outbox.
    /// </summary>
    public void Update(Product aggregate)
    {
        _innerRepository.Update(aggregate);
    }

    /// <summary>
    ///     Removes a product (write operation).
    ///     Cache invalidation is handled via domain events and Wolverine's outbox.
    /// </summary>
    public void Remove(Product aggregate)
    {
        _innerRepository.Remove(aggregate);
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

}
