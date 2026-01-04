#nullable enable

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Handlers;
using NetCommerce.SharedKernel.Domain;
using Shouldly;
using System.Text;

namespace NetCommerce.Integration.Tests.Catalog;

public class ProductCacheInvalidationHandlerTests
{
    private static IDistributedCache CreateCache() => new InMemoryDistributedCache();

    [Fact]
    public async Task ProductUpdatedEvent_ShouldRemoveAllKeys()
    {
        var cache = CreateCache();
        var productId = Guid.NewGuid();
        var cacheKeys = new[]
        {
            $"catalog:product:id:{productId}",
            "catalog:product:sku:OLD-SKU",
            "catalog:product:sku:NEW-SKU",
            "catalog:product:slug:old-slug",
            "catalog:product:slug:new-slug"
        };

        foreach (var key in cacheKeys)
            await cache.SetAsync(key, Encoding.UTF8.GetBytes("cached-value"), new DistributedCacheEntryOptions());

        await ProductCacheInvalidationHandler.Handle(
            new ProductUpdatedDomainEvent(productId, "Name", "OLD-SKU", "NEW-SKU", "old-slug", "new-slug"),
            cache,
            NullLogger.Instance,
            CancellationToken.None);

        foreach (var key in cacheKeys)
        {
            var cached = await cache.GetAsync(key);
            cached.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ProductPriceChangedEvent_ShouldRemoveAllKeys()
    {
        var cache = CreateCache();
        var productId = Guid.NewGuid();
        var cacheKeys = new[]
        {
            $"catalog:product:id:{productId}",
            "catalog:product:sku:SKU-1",
            "catalog:product:slug:my-slug"
        };

        foreach (var key in cacheKeys)
            await cache.SetAsync(key, Encoding.UTF8.GetBytes("cached-value"), new DistributedCacheEntryOptions());

        await ProductCacheInvalidationHandler.Handle(
            new ProductPriceChangedDomainEvent(productId, "SKU-1", "my-slug", Money.Create(5m), Money.Create(10m)),
            cache,
            NullLogger.Instance,
            CancellationToken.None);

        foreach (var key in cacheKeys)
        {
            var cached = await cache.GetAsync(key);
            cached.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ProductUpdatedEvent_ShouldHandleMissingCacheEntry()
    {
        var cache = CreateCache();
        var productId = Guid.NewGuid();
        var cacheKey = $"catalog:product:id:{productId}";

        await ProductCacheInvalidationHandler.Handle(
            new ProductUpdatedDomainEvent(productId, "Name", "OLD-SKU", "NEW-SKU", "old-slug", "new-slug"),
            cache,
            NullLogger.Instance,
            CancellationToken.None);

        var cached = await cache.GetAsync(cacheKey);
        cached.ShouldBeNull();
    }

    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _store[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
