#nullable enable

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Handlers;
using NetCommerce.SharedKernel.Domain;
using Shouldly;

namespace NetCommerce.Integration.Tests.Catalog;

public class ProductCacheInvalidationHandlerTests
{
    private static async Task<HybridCache> CreateCacheAsync()
    {
        var services = new ServiceCollection();
        #pragma warning disable EXTEXP0018
        services.AddHybridCache();
        #pragma warning restore EXTEXP0018
        services.AddDistributedMemoryCache();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<HybridCache>();
    }

    [Fact]
    public async Task ProductUpdatedEvent_ShouldRemoveAllKeys()
    {
        var cache = await CreateCacheAsync();
        var productId = Guid.NewGuid();

        // Seed cache with tags matching what CachedProductRepository uses
        // Note: The keys here are just for verification, the tags are what matters for invalidation.
        // CachedProductRepository uses:
        // GetById: tags: ["catalog", $"product-{id}"]
        // GetBySku: tags: ["catalog", $"product-sku-{sku}"]
        // GetBySlug: tags: ["catalog", $"product-slug-{slug}"]

        await cache.SetAsync($"key-id", "value", tags: [$"product-{productId}"]);
        await cache.SetAsync($"key-sku", "value", tags: [$"product-sku-OLD-SKU"]);
        await cache.SetAsync($"key-slug", "value", tags: [$"product-slug-old-slug"]);

        await ProductCacheInvalidationHandler.Handle(
            new ProductUpdatedDomainEvent(productId, "Name", "OLD-SKU", "NEW-SKU", "old-slug", "new-slug"),
            cache,
            NullLogger.Instance,
            CancellationToken.None);

        // Verify they are gone by checking if factory is called
        bool factoryCalled = false;
        await cache.GetOrCreateAsync($"key-id", _ => { factoryCalled = true; return ValueTask.FromResult("new-value"); });
        factoryCalled.ShouldBeTrue("ID key should be invalidated");

        factoryCalled = false;
        await cache.GetOrCreateAsync($"key-sku", _ => { factoryCalled = true; return ValueTask.FromResult("new-value"); });
        factoryCalled.ShouldBeTrue("SKU key should be invalidated");

        factoryCalled = false;
        await cache.GetOrCreateAsync($"key-slug", _ => { factoryCalled = true; return ValueTask.FromResult("new-value"); });
        factoryCalled.ShouldBeTrue("Slug key should be invalidated");
    }

    [Fact]
    public async Task ProductPriceChangedEvent_ShouldRemoveAllKeys()
    {
        var cache = await CreateCacheAsync();
        var productId = Guid.NewGuid();

        await cache.SetAsync($"key-id", "value", tags: [$"product-{productId}"]);
        await cache.SetAsync($"key-sku", "value", tags: [$"product-sku-SKU-1"]);
        await cache.SetAsync($"key-slug", "value", tags: [$"product-slug-my-slug"]);

        await ProductCacheInvalidationHandler.Handle(
            new ProductPriceChangedDomainEvent(productId, "SKU-1", "my-slug", Money.Create(5m), Money.Create(10m)),
            cache,
            NullLogger.Instance,
            CancellationToken.None);

        bool factoryCalled = false;
        await cache.GetOrCreateAsync($"key-id", _ => { factoryCalled = true; return ValueTask.FromResult("new-value"); });
        factoryCalled.ShouldBeTrue("ID key should be invalidated");

        factoryCalled = false;
        await cache.GetOrCreateAsync($"key-sku", _ => { factoryCalled = true; return ValueTask.FromResult("new-value"); });
        factoryCalled.ShouldBeTrue("SKU key should be invalidated");

        factoryCalled = false;
        await cache.GetOrCreateAsync($"key-slug", _ => { factoryCalled = true; return ValueTask.FromResult("new-value"); });
        factoryCalled.ShouldBeTrue("Slug key should be invalidated");
    }
}
