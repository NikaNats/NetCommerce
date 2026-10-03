using NetCommerce.Basket.Application;
using NetCommerce.Domain.Shared;
using Shouldly;

namespace NetCommerce.Domain.Tests.Basket;

/// <summary>
///     The catalog is the sole authority for basket pricing.
/// </summary>
/// <remarks>
/// <para>
///     <c>BasketEndpoints.AddItem</c> used to map <c>Price = request.UnitPrice</c>
///     straight from the request body onto the basket item. A caller could therefore
///     add any product at any price, and the Redis-persisted
///     <c>ShoppingBasket.TotalPrice</c> — the figure checkout would read — reflected
///     it.
/// </para>
/// <para>
///     These tests exercise <see cref="BasketPricer"/>, which is where the rule now
///     lives. The port is faked rather than a real catalog repository, which is the
///     point: <c>Basket.Application</c> has no compile-time dependency on
///     <c>Catalog</c>, so the invariant is provable from the Basket side alone.
/// </para>
/// </remarks>
public class BasketPricerTests
{
    private const decimal CatalogPrice = 249.99m;

    /// <summary>
    ///     A price source that reports exactly what a catalog says, and records
    ///     whether it was ever asked to trust a caller.
    /// </summary>
    private sealed class FakePriceSource(ProductPricing? pricing) : IProductPriceSource
    {
        public int Calls { get; private set; }

        public Task<ProductPricing?> GetPricingAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            Calls += 1;
            return Task.FromResult(
                pricing is { ProductId: var id } && id == productId ? pricing : null);
        }
    }

    private static ProductPricing Published(Guid id, decimal price = CatalogPrice) =>
        new(id, "Trench Coat", "SKU-TRENCH", Money.Create(price, "GBP"), "images/trench.jpg");

    [Fact]
    public async Task CreateLineAsync_takes_price_from_the_catalog()
    {
        var id = Guid.NewGuid();
        var source = new FakePriceSource(Published(id));

        var line = await BasketPricer.CreateLineAsync(source, id, 1);

        line.Price.ShouldBe(CatalogPrice);
        line.ProductName.ShouldBe("Trench Coat");
        line.Sku.ShouldBe("SKU-TRENCH");
        line.ImageUrl.ShouldBe("images/trench.jpg");
        line.Quantity.ShouldBe(1);
    }

    [Fact]
    public async Task CreateLineAsync_carries_no_way_to_supply_a_price()
    {
        // Not a runtime assertion but a structural one, stated as a test because it
        // is the property that matters: there is NO price parameter. A future
        // endpoint cannot reintroduce the hole by passing one, because there is
        // nowhere to pass it from.
        var method = typeof(BasketPricer).GetMethod(nameof(BasketPricer.CreateLineAsync))!;
        var parameters = method.GetParameters().Select(p => p.Name).ToArray();

        parameters.ShouldContain("productId");
        parameters.ShouldContain("quantity");
        parameters.ShouldNotContain("unitPrice");
        parameters.ShouldNotContain("price");
    }

    [Fact]
    public async Task CreateLineAsync_ignores_a_price_the_catalog_does_not_state()
    {
        // The catalog returned 249.99. Even presented alongside a caller claiming
        // 0.01, the stored line is the catalog figure — the tampered value has
        // nowhere to enter the computation.
        var id = Guid.NewGuid();
        var source = new FakePriceSource(Published(id, CatalogPrice));
        const decimal tampered = 0.01m;

        var line = await BasketPricer.CreateLineAsync(source, id, 3);

        line.Price.ShouldBe(CatalogPrice);
        line.Price.ShouldNotBe(tampered);

        // And the basket total follows the catalog, not the claim.
        var basket = ShoppingBasket.Create("customer-1");
        basket.AddItem(line);
        basket.TotalPrice.ShouldBe(CatalogPrice * 3);
        basket.TotalPrice.ShouldNotBe(tampered * 3);
    }

    [Fact]
    public async Task CreateLineAsync_rejects_an_unknown_product()
    {
        // No catalog entry means no authoritative price, so nothing may be added.
        var source = new FakePriceSource(null);

        await Should.ThrowAsync<BasketPricer.ProductNotSellableException>(
            () => BasketPricer.CreateLineAsync(source, Guid.NewGuid(), 1));
    }

    [Fact]
    public async Task CreateLineAsync_does_not_resolve_a_different_product()
    {
        // A caller asking for an id the source knows about something else gets
        // nothing, rather than the other product's price.
        var known = Guid.NewGuid();
        var source = new FakePriceSource(Published(known));

        await Should.ThrowAsync<BasketPricer.ProductNotSellableException>(
            () => BasketPricer.CreateLineAsync(source, Guid.NewGuid(), 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateLineAsync_rejects_a_non_positive_quantity(int quantity)
    {
        var id = Guid.NewGuid();
        var source = new FakePriceSource(Published(id));

        // A zero or negative quantity would otherwise reach the basket and corrupt
        // the total via ShoppingBasket.AddItem's merge path.
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => BasketPricer.CreateLineAsync(source, id, quantity));
    }

    [Fact]
    public async Task CreateLineAsync_asks_the_source_exactly_once()
    {
        // Guards against an N+1 introduced later: pricing must be a single lookup,
        // and the basket must not re-resolve the product to display it.
        var id = Guid.NewGuid();
        var source = new FakePriceSource(Published(id));

        await BasketPricer.CreateLineAsync(source, id, 2);

        source.Calls.ShouldBe(1);
    }
}