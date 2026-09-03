#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using NetCommerce.Domain.Shared;
using NetCommerce.Integration.Tests.Fixtures;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Chaos;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "ColdStartResilience")]
public sealed class ColdStartCacheDepletionTests : IntegrationTestBase
{
    public ColdStartCacheDepletionTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     Serializable catalog read-model payload — the shape the read path
    ///     must cache. Aggregates (<see cref="Product"/>) are intentionally not
    ///     used here; see <see cref="ProductAggregateCaching_KnownSerializationGap"/>.
    /// </summary>
    private sealed record ProductCardDto(Guid Id, string Name, decimal PriceAmount, string PriceCurrency);

    [Fact]
    public async Task ColdStart_UnderConcurrentStampede_MustExecuteDatabaseQueryExactlyOnce()
    {
        var productId = Guid.NewGuid();
        const int concurrentCallers = 100;

        // 1. Seed product in PostgreSQL
        await using (var catalogDb = Fixture.CreateCatalogDbContext())
        {
            var product = Product.Create(
                name: "Stampede Guard Monitor",
                description: "4K OLED Gaming Monitor",
                sku: $"MON-{Guid.NewGuid():N}"[..12],
                price: Money.Create(799.99m, "GEL"),
                categoryId: Guid.NewGuid());

            product.Publish();
            catalogDb.Products.Add(product);
            await catalogDb.SaveChangesAsync();
            productId = product.Id;
        }

        // 2. Clear Redis cache completely (Cold-Start Simulation).
        // Best-effort hygiene on the shared container; the authoritative cold
        // state comes from the fresh HybridCache provider built below, which
        // starts with empty L1 + L2 by construction.
        try
        {
            await using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(Fixture.RedisConnectionString);
            foreach (var endpoint in redis.GetEndPoints())
            {
                await redis.GetServer(endpoint).FlushDatabaseAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StampedeGuard] Redis flush skipped (non-fatal): {ex.Message}");
        }

        await using var cacheProvider = BuildColdHybridCache();
        var hybridCache = cacheProvider.GetRequiredService<HybridCache>();

        // 3. Spy on the PostgreSQL-backed factory invocation count.
        // The factory performs a real PostgreSQL read with realistic latency,
        // mirroring the catalog read path behind CachedProductRepository.
        var dbQueryCounter = 0;
        var cacheKey = $"catalog:product:id:{productId}";

        async ValueTask<ProductCardDto?> LoadFromPostgres(CancellationToken ct)
        {
            Interlocked.Increment(ref dbQueryCounter);
            await Task.Delay(100, ct); // Simulate realistic 100ms database query latency
            await using var db = Fixture.CreateCatalogDbContext();
            return await db.Products.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => new ProductCardDto(p.Id, p.Name, p.Price.Amount, p.Price.Currency))
                .FirstOrDefaultAsync(ct);
        }

        // 4. ACT: 100 concurrent requests hit the cold cache simultaneously
        var startBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, concurrentCallers).Select(async _ =>
        {
            await startBarrier.Task;
            return await hybridCache.GetOrCreateAsync(
                cacheKey,
                LoadFromPostgres,
                new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromHours(1),
                    LocalCacheExpiration = TimeSpan.FromMinutes(5)
                },
                tags: ["catalog", $"product-{productId}"]);
        }).ToList();

        startBarrier.SetResult();
        var results = await Task.WhenAll(tasks);

        // 5. ASSERT: Stampede Protection Invariants
        results.Length.ShouldBe(concurrentCallers);
        results.All(p => p != null && p.Id == productId).ShouldBeTrue();

        // HybridCache single-flights the key during factory execution, ensuring exactly 1 DB round-trip
        dbQueryCounter.ShouldBe(1,
            $"CACHE STAMPEDE DETECTED: Database was hit {dbQueryCounter} times for the same cold key under concurrency. Expected exactly 1.");

        Console.WriteLine($"[StampedeGuard] 100 concurrent requests coalesced into {dbQueryCounter} database read.");
    }

    /// <summary>
    ///     REGRESSION: the <see cref="Product"/> aggregate must round-trip through
    ///     <c>CachedProductRepository</c> + HybridCache. HybridCache serializes
    ///     entries even on the in-process stampede path, and aggregates expose no
    ///     deserializable constructor by design — <c>ProductCacheSerializer</c>
    ///     bridges the gap via snapshot DTO. A rehydrated hit must equal the
    ///     database state field-for-field, raise no domain events, and be served
    ///     from cache on the second read.
    /// </summary>
    [Fact]
    public async Task ProductAggregateCaching_MustRoundTripThroughHybridCache()
    {
        var productId = Guid.NewGuid();

        await using (var catalogDb = Fixture.CreateCatalogDbContext())
        {
            var product = Product.Create(
                name: "Cache Round-Trip Probe",
                description: "Probe product",
                sku: $"PRB-{Guid.NewGuid():N}"[..12],
                price: Money.Create(10.00m, "GEL"),
                categoryId: Guid.NewGuid());

            product.Publish();
            product.AddImage("products/probe/main.jpg", displayOrder: 0, isPrimary: true);
            product.AddAttribute("color", "red", "Color");
            catalogDb.Products.Add(product);
            await catalogDb.SaveChangesAsync();
            productId = product.Id;
        }

        var dbQueryCounter = 0;
        var innerRepo = Substitute.For<IProductRepository>();
        innerRepo.GetByIdAsync(productId, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                Interlocked.Increment(ref dbQueryCounter);
                await using var db = Fixture.CreateCatalogDbContext();
                return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
            });

        await using var cacheProvider = BuildColdHybridCache();
        var cachedRepo = new CachedProductRepository(
            innerRepo,
            cacheProvider.GetRequiredService<HybridCache>());

        var first = await cachedRepo.GetByIdAsync(productId);
        var second = await cachedRepo.GetByIdAsync(productId);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();

        // Field-for-field fidelity with database state
        second.Id.ShouldBe(first.Id);
        second.Name.ShouldBe(first.Name);
        second.Description.ShouldBe(first.Description);
        second.Sku.ShouldBe(first.Sku);
        second.Price.Amount.ShouldBe(first.Price.Amount);
        second.Price.Currency.ShouldBe(first.Price.Currency);
        second.Status.ShouldBe(first.Status);
        second.Slug.ShouldBe(first.Slug);
        second.Images.Count.ShouldBe(1);
        second.Images[0].ImageKey.ShouldBe("products/probe/main.jpg");
        second.Images[0].IsPrimary.ShouldBeTrue();
        second.Attributes.Count.ShouldBe(1);
        second.Attributes[0].Key.ShouldBe("color");
        second.Attributes[0].Value.ShouldBe("red");

        // Cache hits must not republish domain events
        first.DomainEvents.ShouldBeEmpty();
        second.DomainEvents.ShouldBeEmpty();

        // Second read served from cache — exactly one database round-trip
        dbQueryCounter.ShouldBe(1);
    }

    /// <summary>
    ///     Builds an isolated HybridCache provider with empty L1 + L2,
    ///     simulating a cold start without touching the shared test host.
    ///     Mirrors <c>ProductCacheInvalidationHandlerTests.CreateCacheAsync</c>
    ///     plus the <c>ProductCacheSerializer</c> wiring from
    ///     <c>CatalogModule</c> (required: aggregates are not STJ-serializable).
    /// </summary>
    private static ServiceProvider BuildColdHybridCache()
    {
        var services = new ServiceCollection();
#pragma warning disable EXTEXP0018 // HybridCache is experimental in this SDK band; accepted for tests
        services.AddHybridCache().AddSerializer<Product?>(
            new NetCommerce.Catalog.Infrastructure.Caching.ProductCacheSerializer());
#pragma warning restore EXTEXP0018
        services.AddDistributedMemoryCache();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
