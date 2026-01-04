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
            .Returns((string?)null);
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
            .Returns(ci => callCount <= 1 ? null : NotFoundSentinel);

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
            .Returns((string?)null);
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
            .Returns((string?)null);
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
            .Returns((string?)null);
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
