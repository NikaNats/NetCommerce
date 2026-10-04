using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Services;
using NetCommerce.Domain.Shared;
using NetCommerce.Integration.Tests.Fixtures;
using Shouldly;

namespace NetCommerce.Integration.Tests.Catalog;

/// <summary>
///     ACL translation contract for <see cref="OrderingPriceLookup"/> (distilled DDD).
/// </summary>
/// <remarks>
/// <para>
///     Ordering taxes by category (<c>ITaxProvider.GetTaxAsync(_, _, category)</c>),
///     so the category NAME is part of the <see cref="PriceSnapshot"/> contract.
///     The lookup previously projected everything EXCEPT the category, silently
///     disabling every category tax rule. These tests pin the translation:
///     name present when the category exists, null (not failure) when it does not.
/// </para>
/// </remarks>
[Trait("Category", "RequiresDocker")]
public sealed class OrderingPriceLookupTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetPricesAsync_ShouldIncludeCategoryName()
    {
        await using var context = Fixture.CreateCatalogDbContext();

        var category = Category.Create("Food", "Edible goods");
        context.Categories.Add(category);
        var product = Product.Create(
            "Apple", "Fresh apple", "SKU-APPLE", Money.Create(2.5m, "GEL"), category.Id);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        using var scope = Fixture.Host.Services.CreateScope();
        var lookup = new OrderingPriceLookup(scope.ServiceProvider);

        var snapshots = await lookup.GetPricesAsync([product.Id]);

        snapshots.Count.ShouldBe(1);
        snapshots[product.Id].Name.ShouldBe("Apple");
        snapshots[product.Id].Category.ShouldBe("Food");
    }

    [Fact]
    public async Task GetPricesAsync_MissingCategory_ShouldPriceWithNullCategory()
    {
        await using var context = Fixture.CreateCatalogDbContext();

        // No category row: the product must still price (left join), with
        // Category null so the tax provider falls back to the base rate.
        var product = Product.Create(
            "Mystery", "No category", "SKU-MYSTERY", Money.Create(9.99m, "GEL"), Guid.NewGuid());
        context.Products.Add(product);
        await context.SaveChangesAsync();

        using var scope = Fixture.Host.Services.CreateScope();
        var lookup = new OrderingPriceLookup(scope.ServiceProvider);

        var snapshots = await lookup.GetPricesAsync([product.Id]);

        snapshots.Count.ShouldBe(1);
        snapshots[product.Id].Category.ShouldBeNull();
    }
}
