using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Domain;
using NSubstitute;
using Shouldly;
using Xunit;

namespace NetCommerce.Catalog.Tests.Infrastructure;

/// <summary>
///     Tests for CachedProductRepository negative caching (Cache Penetration defense).
///     Validates the "Shielded Negative Caching" pattern implementation.
/// </summary>
public class CachedProductRepositorySecurityTests
{
    private const string NotFoundSentinel = "SENTINEL_NOT_FOUND";
    private readonly IDistributedCache _mockCache;
    private readonly IProductRepository _mockInnerRepo;
    private readonly CachedProductRepository _sut;

    public CachedProductRepositorySecurityTests()
    {
        _mockCache = Substitute.For<IDistributedCache>();
        _mockInnerRepo = Substitute.For<IProductRepository>();
        _sut = new CachedProductRepository(_mockInnerRepo, _mockCache);
    }

    #region Cache Penetration Defense Tests

    [Fact]
    public async Task GetByIdAsync_NonExistentProduct_ShouldCacheNegativeResult()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        _mockCache.GetStringAsync(Arg.Any<string>(), default)
            .Returns(Task.FromResult<string?>(null));
        _mockInnerRepo.GetByIdAsync(nonExistentId, default)
            .Returns((Product?)null);

        // Act - First request (cache miss, DB query)
        var result = await _sut.GetByIdAsync(nonExistentId);

        // Assert - Should return null
        result.ShouldBeNull();

        // Assert - Should cache the sentinel value (CRITICAL for security)
        await _mockCache.Received(1).SetStringAsync(
            $"catalog:product:id:{nonExistentId}",
            NotFoundSentinel,
            Arg.Is<DistributedCacheEntryOptions>(opt =>
                opt.AbsoluteExpirationRelativeToNow == TimeSpan.FromSeconds(300)),
            default);
    }

    [Fact]
    public async Task GetByIdAsync_CachedNegativeResult_ShouldNotHitDatabase()
    {
        // Arrange - Sentinel already in cache
        var nonExistentId = Guid.NewGuid();
        var cacheKey = $"catalog:product:id:{nonExistentId}";

        _mockCache.GetStringAsync(cacheKey, default)
            .Returns(NotFoundSentinel);

        // Act - Request with cached negative result
        var result = await _sut.GetByIdAsync(nonExistentId);

        // Assert - Should return null
        result.ShouldBeNull();

        // Assert - Should NOT hit database (CRITICAL: prevents DB DoS)
        await _mockInnerRepo.DidNotReceive().GetByIdAsync(nonExistentId, default);
    }

    [Fact]
    public async Task GetByIdAsync_CachePenetrationAttack_ShouldBlockAfterFirstQuery()
    {
        // Arrange - Simulate attacker sending 3 requests with same fake ID
        var fakeId = Guid.NewGuid();
        var cacheKey = $"catalog:product:id:{fakeId}";

        // Setup: First call returns null (miss), second call returns sentinel (hit)
        var callCount = 0;
        _mockCache.When(x => x.GetStringAsync(cacheKey, default))
            .Do(_ => callCount++);

        _mockCache.GetStringAsync(cacheKey, default)
            .Returns(_ => Task.FromResult<string?>(callCount <= 1 ? null : NotFoundSentinel));

        _mockInnerRepo.GetByIdAsync(fakeId, default)
            .Returns((Product?)null);

        // Act - Simulate 3 requests (representing attack pattern)
        var result1 = await _sut.GetByIdAsync(fakeId);
        var result2 = await _sut.GetByIdAsync(fakeId);
        var result3 = await _sut.GetByIdAsync(fakeId);

        // Assert - All should return null
        result1.ShouldBeNull();
        result2.ShouldBeNull();
        result3.ShouldBeNull();

        // Assert - Database should only be hit ONCE (CRITICAL for DoS prevention)
        await _mockInnerRepo.Received(1).GetByIdAsync(fakeId, default);

        // Assert - Sentinel cached on first request
        await _mockCache.Received(1).SetStringAsync(
            cacheKey,
            NotFoundSentinel,
            Arg.Any<DistributedCacheEntryOptions>(),
            default);
    }

    [Fact]
    public async Task GetBySkuAsync_NonExistentSku_ShouldCacheNegativeResult()
    {
        // Arrange - Simulate SKU scanner bot
        var fakeSku = "NONEXISTENT-SKU-999";
        _mockCache.GetStringAsync(Arg.Any<string>(), default)
            .Returns(Task.FromResult<string?>(null));
        _mockInnerRepo.GetBySkuAsync(fakeSku, default)
            .Returns((Product?)null);

        // Act
        var result = await _sut.GetBySkuAsync(fakeSku);

        // Assert
        result.ShouldBeNull();
        await _mockCache.Received(1).SetStringAsync(
            $"catalog:product:sku:{fakeSku}",
            NotFoundSentinel,
            Arg.Is<DistributedCacheEntryOptions>(opt =>
                opt.AbsoluteExpirationRelativeToNow == TimeSpan.FromSeconds(300)),
            default);
    }

    [Fact]
    public async Task GetBySlugAsync_NonExistentSlug_ShouldCacheNegativeResult()
    {
        // Arrange - Simulate URL scanner / SEO bot
        var fakeSlug = "nonexistent-product-url";
        _mockCache.GetStringAsync(Arg.Any<string>(), default)
            .Returns(Task.FromResult<string?>(null));
        _mockInnerRepo.GetBySlugAsync(fakeSlug, default)
            .Returns((Product?)null);

        // Act
        var result = await _sut.GetBySlugAsync(fakeSlug);

        // Assert - CRITICAL for public-facing product pages
        result.ShouldBeNull();
        await _mockCache.Received(1).SetStringAsync(
            $"catalog:product:slug:{fakeSlug}",
            NotFoundSentinel,
            Arg.Is<DistributedCacheEntryOptions>(opt =>
                opt.AbsoluteExpirationRelativeToNow == TimeSpan.FromSeconds(300)),
            default);
    }

    [Fact]
    public async Task GetByIdAsync_NegativeCacheTtl_ShouldBe5Minutes()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        _mockCache.GetStringAsync(Arg.Any<string>(), default)
            .Returns(Task.FromResult<string?>(null));
        _mockInnerRepo.GetByIdAsync(nonExistentId, default)
            .Returns((Product?)null);

        // Act
        await _sut.GetByIdAsync(nonExistentId);

        // Assert - TTL should be 5 minutes (300 seconds)
        // Not too short (DB still hit frequently)
        // Not too long (fills Redis with junk)
        await _mockCache.Received(1).SetStringAsync(
            Arg.Any<string>(),
            NotFoundSentinel,
            Arg.Is<DistributedCacheEntryOptions>(opt =>
                opt.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(5)),
            default);
    }

    #endregion

    #region Additional Security Tests

    [Fact]
    public async Task GetByIdAsync_HighVolumeAttack_ShouldOnlyHitDatabaseOnce()
    {
        // Arrange - Simulate DDoS attack with 1000 requests for non-existent product
        var fakeId = Guid.NewGuid();
        var cacheKey = $"catalog:product:id:{fakeId}";

        var callCount = 0;
        _mockCache.GetStringAsync(cacheKey, default)
            .Returns(ci =>
            {
                callCount++;
                return callCount == 1 ? null : NotFoundSentinel;
            });

        _mockInnerRepo.GetByIdAsync(fakeId, default)
            .Returns((Product?)null);

        // Act - Simulate 1000 requests
        var tasks = Enumerable.Range(0, 1000)
            .Select(_ => _sut.GetByIdAsync(fakeId));

        var results = await Task.WhenAll(tasks);

        // Assert - All return null
        results.ShouldAllBe(r => r == null);

        // Assert - Database hit only once (CRITICAL)
        await _mockInnerRepo.Received(1).GetByIdAsync(fakeId, default);
    }

    [Fact]
    public async Task GetBySkuAsync_BotScanning_ShouldBlockAfterFirstQuery()
    {
        // Arrange - Simulate bot scanning with common SKU patterns
        var skuPatterns = new[] { "PROD-001", "ITEM-999", "SKU-FAKE" };

        foreach (var sku in skuPatterns)
        {
            _mockCache.GetStringAsync($"catalog:product:sku:{sku}", default)
                .Returns(Task.FromResult<string?>(null), Task.FromResult<string?>(NotFoundSentinel), Task.FromResult<string?>(NotFoundSentinel));

            _mockInnerRepo.GetBySkuAsync(sku, default)
                .Returns((Product?)null);
        }

        // Act - Bot tries each SKU 3 times
        foreach (var sku in skuPatterns)
        {
            await _sut.GetBySkuAsync(sku);
            await _sut.GetBySkuAsync(sku);
            await _sut.GetBySkuAsync(sku);
        }

        // Assert - Each SKU queried only once
        foreach (var sku in skuPatterns)
        {
            await _mockInnerRepo.Received(1).GetBySkuAsync(sku, default);
        }
    }

    [Fact]
    public async Task GetBySlugAsync_SeoSpider_ShouldCacheNegativeResults()
    {
        // Arrange - Simulate SEO spider crawling invalid URLs
        var fakeUrls = new[]
        {
            "nonexistent-product",
            "old-discontinued-item",
            "test-product-removed"
        };

        foreach (var slug in fakeUrls)
        {
            _mockCache.GetStringAsync($"catalog:product:slug:{slug}", default)
                .Returns((string?)null);

            _mockInnerRepo.GetBySlugAsync(slug, default)
                .Returns((Product?)null);
        }

        // Act
        foreach (var slug in fakeUrls)
        {
            await _sut.GetBySlugAsync(slug);
        }

        // Assert - All cached with sentinel
        foreach (var slug in fakeUrls)
        {
            await _mockCache.Received(1).SetStringAsync(
                $"catalog:product:slug:{slug}",
                NotFoundSentinel,
                Arg.Any<DistributedCacheEntryOptions>(),
                default);
        }
    }

    [Fact]
    public async Task GetByIdAsync_RapidFireRequests_ShouldHandleRaceCondition()
    {
        // Arrange - Simulate multiple simultaneous requests (race condition)
        var fakeId = Guid.NewGuid();
        var cacheKey = $"catalog:product:id:{fakeId}";

        _mockCache.GetStringAsync(cacheKey, default)
            .Returns(Task.FromResult<string?>(null)); // Always return null (worst case)

        _mockInnerRepo.GetByIdAsync(fakeId, default)
            .Returns((Product?)null);

        // Act - 100 concurrent requests
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _sut.GetByIdAsync(fakeId));

        await Task.WhenAll(tasks);

        // Assert - Database called multiple times but cache should be set
        await _mockCache.Received().SetStringAsync(
            cacheKey,
            NotFoundSentinel,
            Arg.Any<DistributedCacheEntryOptions>(),
            default);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingProduct_ShouldCacheNormally()
    {
        // Arrange - Ensure existing products still work correctly
        var existingId = Guid.NewGuid();
        var existingProduct = CreateTestProduct(existingId);
        var cacheKey = $"catalog:product:id:{existingId}";

        _mockCache.GetStringAsync(cacheKey, default)
            .Returns(Task.FromResult<string?>(null));

        _mockInnerRepo.GetByIdAsync(existingId, default)
            .Returns(existingProduct);

        // Act
        var result = await _sut.GetByIdAsync(existingId);

        // Assert - Should return product
        result.ShouldNotBeNull();
        result.Id.ShouldBe(existingId);

        // Assert - Should cache the product JSON (NOT sentinel)
        await _mockCache.Received(1).SetStringAsync(
            cacheKey,
            Arg.Is<string>(s => s != NotFoundSentinel && s.Contains(existingId.ToString())),
            Arg.Any<DistributedCacheEntryOptions>(),
            default);
    }

    [Fact]
    public async Task GetByIdAsync_CacheEviction_ShouldRecacheOnNextRequest()
    {
        // Arrange - Sentinel expires after 5 minutes
        var fakeId = Guid.NewGuid();
        var cacheKey = $"catalog:product:id:{fakeId}";

        // First request: cache miss, DB query, cache sentinel
        _mockCache.GetStringAsync(cacheKey, default)
            .Returns(Task.FromResult<string?>(null));

        _mockInnerRepo.GetByIdAsync(fakeId, default)
            .Returns((Product?)null);

        // Act - First request
        await _sut.GetByIdAsync(fakeId);

        // Simulate cache eviction (5 minutes passed)
        _mockCache.GetStringAsync(cacheKey, default)
            .Returns(Task.FromResult<string?>(null));

        // Act - Second request after eviction
        await _sut.GetByIdAsync(fakeId);

        // Assert - Database hit twice (once per cache miss)
        await _mockInnerRepo.Received(2).GetByIdAsync(fakeId, default);

        // Assert - Sentinel cached twice
        await _mockCache.Received(2).SetStringAsync(
            cacheKey,
            NotFoundSentinel,
            Arg.Any<DistributedCacheEntryOptions>(),
            default);
    }

    [Fact]
    public async Task GetByIdAsync_DifferentNonExistentIds_ShouldCacheEachSeparately()
    {
        // Arrange - Multiple different fake IDs
        var fakeIds = Enumerable.Range(0, 10)
            .Select(_ => Guid.NewGuid())
            .ToList();

        foreach (var id in fakeIds)
        {
            _mockCache.GetStringAsync($"catalog:product:id:{id}", default)
                .Returns(Task.FromResult<string?>(null));

            _mockInnerRepo.GetByIdAsync(id, default)
                .Returns((Product?)null);
        }

        // Act - Query all fake IDs
        foreach (var id in fakeIds)
        {
            await _sut.GetByIdAsync(id);
        }

        // Assert - Each ID cached separately
        foreach (var id in fakeIds)
        {
            await _mockCache.Received(1).SetStringAsync(
                $"catalog:product:id:{id}",
                NotFoundSentinel,
                Arg.Any<DistributedCacheEntryOptions>(),
                default);
        }

        // Assert - Each ID queried once
        foreach (var id in fakeIds)
        {
            await _mockInnerRepo.Received(1).GetByIdAsync(id, default);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("FAKE-SKU")]
    [InlineData("BOT-SCANNER-123")]
    public async Task GetBySkuAsync_VariousInvalidSkus_ShouldCacheAll(string invalidSku)
    {
        // Arrange
        _mockCache.GetStringAsync(Arg.Any<string>(), default)
            .Returns(Task.FromResult<string?>(null));

        _mockInnerRepo.GetBySkuAsync(invalidSku, default)
            .Returns((Product?)null);

        // Act
        await _sut.GetBySkuAsync(invalidSku);

        // Assert
        await _mockCache.Received(1).SetStringAsync(
            $"catalog:product:sku:{invalidSku}",
            NotFoundSentinel,
            Arg.Any<DistributedCacheEntryOptions>(),
            default);
    }

    [Fact]
    public async Task GetBySlugAsync_CaseSensitiveUrls_ShouldCacheSeparately()
    {
        // Arrange - URLs might differ by case
        var slugLower = "product-name";
        var slugUpper = "PRODUCT-NAME";
        var slugMixed = "Product-Name";

        foreach (var slug in new[] { slugLower, slugUpper, slugMixed })
        {
            _mockCache.GetStringAsync($"catalog:product:slug:{slug}", default)
                .Returns(Task.FromResult<string?>(null));

            _mockInnerRepo.GetBySlugAsync(slug, default)
                .Returns((Product?)null);
        }

        // Act
        await _sut.GetBySlugAsync(slugLower);
        await _sut.GetBySlugAsync(slugUpper);
        await _sut.GetBySlugAsync(slugMixed);

        // Assert - Each cached separately (case-sensitive caching)
        foreach (var slug in new[] { slugLower, slugUpper, slugMixed })
        {
            await _mockCache.Received(1).SetStringAsync(
                $"catalog:product:slug:{slug}",
                NotFoundSentinel,
                Arg.Any<DistributedCacheEntryOptions>(),
                default);
        }
    }

    #endregion

    #region Helper Methods

    private Product CreateTestProduct(Guid id)
    {
        var product = Product.Create(
            "Test Product",
            "Test product for unit tests",
            "TEST-SKU-001",
            Money.Create(1000m, "USD"),
            Guid.NewGuid());

        // Use reflection to set the ID for testing purposes
        var idProperty = typeof(Product).GetProperty("Id");
        idProperty?.SetValue(product, id);

        return product;
    }

    #endregion
}
