using Microsoft.EntityFrameworkCore;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.SharedKernel.Domain;
using Shouldly;

namespace NetCommerce.Integration.Tests.Catalog;

/// <summary>
///     Integration tests for Catalog module repository operations.
///     Uses Testcontainers PostgreSQL with Respawn for database cleanup.
/// </summary>
[Trait("Category", "RequiresDocker")]
public class CatalogRepositoryTests : IntegrationTestBase
{
    public CatalogRepositoryTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Product Images Tests

    [Fact]
    public async Task AddProductImage_ShouldPersistWithProduct()
    {
        // Arrange
        await using var context = Fixture.CreateCatalogDbContext();

        var product = Product.Create(
            "Product with Images",
            "Description",
            "IMG-001",
            Money.Create(100m),
            Guid.NewGuid());

        product.AddImage("images/primary.jpg", 1, true);
        product.AddImage("images/secondary.jpg", 2);

        // Act
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateCatalogDbContext();
        var savedProduct = await verifyContext.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == product.Id);

        savedProduct.ShouldNotBeNull();
        savedProduct.Images.Count.ShouldBe(2);
        savedProduct.Images.Count(i => i.IsPrimary).ShouldBe(1);
    }

    #endregion

    #region Product Attributes Tests

    [Fact]
    public async Task AddProductAttributes_ShouldPersistWithProduct()
    {
        // Arrange
        await using var context = Fixture.CreateCatalogDbContext();

        var product = Product.Create(
            "Product with Attributes",
            "Description",
            "ATTR-001",
            Money.Create(100m),
            Guid.NewGuid());

        product.AddAttribute("Color", "Black");
        product.AddAttribute("Storage", "1TB", "Storage Capacity");

        // Act
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateCatalogDbContext();
        var savedProduct = await verifyContext.Products
            .Include(p => p.Attributes)
            .FirstOrDefaultAsync(p => p.Id == product.Id);

        savedProduct.ShouldNotBeNull();
        savedProduct.Attributes.Count.ShouldBe(2);
        savedProduct.Attributes.Any(a => a.Key == "Color" && a.Value == "Black").ShouldBeTrue();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task OptimisticConcurrency_ShouldDetectConflicts()
    {
        // Arrange
        await using var context1 = Fixture.CreateCatalogDbContext();

        var product = Product.Create("Concurrent Test", "d", "CONC-1", Money.Create(100m), Guid.NewGuid());
        context1.Products.Add(product);
        await context1.SaveChangesAsync();
        var productId = product.Id;

        // Act - Two contexts load the same entity
        await using var context2 = Fixture.CreateCatalogDbContext();
        var product1 = await context1.Products.FindAsync(productId);
        var product2 = await context2.Products.FindAsync(productId);

        // First update succeeds
        product1!.UpdateDetails("Update 1", "d", "CONC-1");
        await context1.SaveChangesAsync();

        // Second update should fail due to concurrency
        product2!.UpdateDetails("Update 2", "d", "CONC-1");

        await Should.ThrowAsync<DbUpdateConcurrencyException>(async () => { await context2.SaveChangesAsync(); });
    }

    #endregion

    #region Product CRUD Tests

    [Fact]
    public async Task AddProduct_ShouldPersistToDatabase()
    {
        // Arrange
        await using var context = Fixture.CreateCatalogDbContext();

        var product = Product.Create(
            "PlayStation 5",
            "Next-gen gaming console",
            "PS5-001",
            Money.Create(499.99m),
            Guid.NewGuid());

        // Act
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateCatalogDbContext();
        var savedProduct = await verifyContext.Products
            .FirstOrDefaultAsync(p => p.Id == product.Id);

        savedProduct.ShouldNotBeNull();
        savedProduct.Name.ShouldBe("PlayStation 5");
        savedProduct.Sku.ShouldBe("PS5-001");
        savedProduct.Price.Amount.ShouldBe(499.99m);
    }

    [Fact]
    public async Task UpdateProduct_ShouldPersistChanges()
    {
        // Arrange
        await using var context = Fixture.CreateCatalogDbContext();

        var product = Product.Create(
            "Original Name",
            "Original Description",
            "SKU-001",
            Money.Create(100m),
            Guid.NewGuid());

        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Act
        product.UpdateDetails("Updated Name", "Updated Description", "SKU-002");
        product.UpdatePrice(Money.Create(150m));
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateCatalogDbContext();
        var updatedProduct = await verifyContext.Products.FindAsync(product.Id);

        updatedProduct.ShouldNotBeNull();
        updatedProduct.Name.ShouldBe("Updated Name");
        updatedProduct.Description.ShouldBe("Updated Description");
        updatedProduct.Sku.ShouldBe("SKU-002");
        updatedProduct.Price.Amount.ShouldBe(150m);
    }

    [Fact]
    public async Task DeleteProduct_ShouldRemoveFromDatabase()
    {
        // Arrange
        await using var context = Fixture.CreateCatalogDbContext();

        var product = Product.Create(
            "To Be Deleted",
            "Description",
            "SKU-DEL",
            Money.Create(50m),
            Guid.NewGuid());

        context.Products.Add(product);
        await context.SaveChangesAsync();
        var productId = product.Id;

        // Act
        context.Products.Remove(product);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateCatalogDbContext();
        var deletedProduct = await verifyContext.Products.FindAsync(productId);
        deletedProduct.ShouldBeNull();
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task QueryProducts_ByStatus_ShouldReturnFiltered()
    {
        // Arrange
        await using var context = Fixture.CreateCatalogDbContext();

        var draftProduct = Product.Create("Draft", "d", "DRAFT-1", Money.Create(10m), Guid.NewGuid());
        var publishedProduct = Product.Create("Published", "p", "PUB-1", Money.Create(20m), Guid.NewGuid());
        publishedProduct.Publish();

        context.Products.AddRange(draftProduct, publishedProduct);
        await context.SaveChangesAsync();

        // Act
        var publishedProducts = await context.Products
            .Where(p => p.Status == ProductStatus.Published)
            .ToListAsync();

        // Assert
        publishedProducts.Count.ShouldBe(1);
        publishedProducts.First().Name.ShouldBe("Published");
    }

    [Fact]
    public async Task QueryProducts_ByPriceRange_ShouldReturnFiltered()
    {
        // Arrange
        await using var context = Fixture.CreateCatalogDbContext();

        var skus = new[] { "CHEAP", "MED", "EXP" };

        context.Products.AddRange(
            Product.Create("Cheap", "d", skus[0], Money.Create(50m), Guid.NewGuid()),
            Product.Create("Medium", "d", skus[1], Money.Create(150m), Guid.NewGuid()),
            Product.Create("Expensive", "d", skus[2], Money.Create(500m), Guid.NewGuid()));

        await context.SaveChangesAsync();

        // Act
        var affordableProducts = await context.Products
            .Where(p => skus.Contains(p.Sku) && p.Price.Amount >= 100m && p.Price.Amount <= 200m)
            .ToListAsync();

        // Assert
        affordableProducts.ShouldHaveSingleItem();
        affordableProducts.First().Name.ShouldBe("Medium");
    }

    #endregion
}
