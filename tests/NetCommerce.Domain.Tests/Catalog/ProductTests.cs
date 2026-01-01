using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Domain.Tests.Fakers;
using NetCommerce.SharedKernel.Domain;
using Shouldly;

namespace NetCommerce.Domain.Tests.Catalog;

/// <summary>
///     Unit tests for Product aggregate.
/// </summary>
public class ProductTests
{
    #region Create Tests

    [Fact]
    public void Create_WithValidData_ShouldCreateProduct()
    {
        // Arrange
        var name = "Test Product";
        var description = "Test Description";
        var sku = "SKU-001";
        var price = Money.Create(99.99m);
        var categoryId = Guid.NewGuid();

        // Act
        var product = Product.Create(name, description, sku, price, categoryId);

        // Assert
        product.ShouldNotBeNull();
        product.Id.ShouldNotBe(Guid.Empty);
        product.Name.ShouldBe(name);
        product.Description.ShouldBe(description);
        product.Sku.ShouldBe(sku);
        product.Price.ShouldBe(price);
        product.CategoryId.ShouldBe(categoryId);
        product.Status.ShouldBe(ProductStatus.Draft);
        product.Slug.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Create_ShouldRaise_ProductCreatedDomainEvent()
    {
        // Act
        var product = ProductFaker.Generate();

        // Assert
        var domainEvents = product.DomainEvents.ToList();
        domainEvents.ShouldContain(e => e is ProductCreatedDomainEvent);

        var createdEvent = domainEvents.OfType<ProductCreatedDomainEvent>().Single();
        createdEvent.ProductId.ShouldBe(product.Id);
        createdEvent.Name.ShouldBe(product.Name);
        createdEvent.Sku.ShouldBe(product.Sku);
    }

    [Fact]
    public void Create_ShouldGenerateSlug_FromName()
    {
        // Arrange
        var name = "My Amazing Product";

        // Act
        var product = Product.Create(name, "desc", "sku", Money.Create(10), Guid.NewGuid());

        // Assert
        product.Slug.ShouldBe("my-amazing-product");
    }

    #endregion

    #region UpdateDetails Tests

    [Fact]
    public void UpdateDetails_ShouldUpdateProductProperties()
    {
        // Arrange
        var product = ProductFaker.Generate();
        var newName = "Updated Name";
        var newDescription = "Updated Description";
        var newSku = "NEW-SKU";

        // Act
        product.UpdateDetails(newName, newDescription, newSku);

        // Assert
        product.Name.ShouldBe(newName);
        product.Description.ShouldBe(newDescription);
        product.Sku.ShouldBe(newSku);
    }

    [Fact]
    public void UpdateDetails_ShouldRaise_ProductUpdatedDomainEvent()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.ClearDomainEvents();

        // Act
        product.UpdateDetails("New Name", "New Desc", "NEW-SKU");

        // Assert
        product.DomainEvents.ShouldContain(e => e is ProductUpdatedDomainEvent);
    }

    #endregion

    #region UpdatePrice Tests

    [Fact]
    public void UpdatePrice_ShouldChangePrice()
    {
        // Arrange
        var product = ProductFaker.Generate();
        var newPrice = Money.Create(199.99m);

        // Act
        product.UpdatePrice(newPrice);

        // Assert
        product.Price.ShouldBe(newPrice);
    }

    [Fact]
    public void UpdatePrice_ShouldRaise_ProductPriceChangedDomainEvent()
    {
        // Arrange
        var product = ProductFaker.Generate();
        var oldPrice = product.Price;
        var newPrice = Money.Create(199.99m);
        product.ClearDomainEvents();

        // Act
        product.UpdatePrice(newPrice);

        // Assert
        var priceChangedEvent = product.DomainEvents
            .OfType<ProductPriceChangedDomainEvent>()
            .Single();

        priceChangedEvent.ProductId.ShouldBe(product.Id);
        priceChangedEvent.OldPrice.ShouldBe(oldPrice);
        priceChangedEvent.NewPrice.ShouldBe(newPrice);
    }

    #endregion

    #region Publish Tests

    [Fact]
    public void Publish_WhenDraft_ShouldChangeStatusToPublished()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.Status.ShouldBe(ProductStatus.Draft);

        // Act
        product.Publish();

        // Assert
        product.Status.ShouldBe(ProductStatus.Published);
    }

    [Fact]
    public void Publish_ShouldRaise_ProductPublishedDomainEvent()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.ClearDomainEvents();

        // Act
        product.Publish();

        // Assert
        product.DomainEvents.ShouldContain(e => e is ProductPublishedDomainEvent);
    }

    [Fact]
    public void Publish_WhenAlreadyPublished_ShouldThrowException()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.Publish();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => product.Publish())
            .Message.ShouldContain("already published");
    }

    #endregion

    #region Archive Tests

    [Fact]
    public void Archive_ShouldChangeStatusToArchived()
    {
        // Arrange
        var product = ProductFaker.Generate();

        // Act
        product.Archive();

        // Assert
        product.Status.ShouldBe(ProductStatus.Archived);
    }

    [Fact]
    public void Archive_ShouldRaise_ProductArchivedDomainEvent()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.ClearDomainEvents();

        // Act
        product.Archive();

        // Assert
        product.DomainEvents.ShouldContain(e => e is ProductArchivedDomainEvent);
    }

    #endregion

    #region Image Tests

    [Fact]
    public void AddImage_ShouldAddImageToCollection()
    {
        // Arrange
        var product = ProductFaker.Generate();
        var imageKey = "images/product-1.jpg";

        // Act
        product.AddImage(imageKey, 1, true);

        // Assert
        product.Images.ShouldHaveSingleItem();
        product.Images.First().ImageKey.ShouldBe(imageKey);
        product.Images.First().IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void AddImage_WithPrimary_ShouldRemovePrimaryFromOthers()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.AddImage("image1.jpg", 1, true);

        // Act
        product.AddImage("image2.jpg", 2, true);

        // Assert
        product.Images.Count.ShouldBe(2);
        product.Images.Count(i => i.IsPrimary).ShouldBe(1);
        product.Images.Last().IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void RemoveImage_ShouldRemoveFromCollection()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.AddImage("image1.jpg", 1);
        var imageId = product.Images.First().Id;

        // Act
        product.RemoveImage(imageId);

        // Assert
        product.Images.ShouldBeEmpty();
    }

    #endregion

    #region Attribute Tests

    [Fact]
    public void AddAttribute_ShouldAddAttributeToCollection()
    {
        // Arrange
        var product = ProductFaker.Generate();

        // Act
        product.AddAttribute("Color", "Red", "Product Color");

        // Assert
        product.Attributes.ShouldHaveSingleItem();
        product.Attributes.First().Key.ShouldBe("Color");
        product.Attributes.First().Value.ShouldBe("Red");
    }

    [Fact]
    public void RemoveAttribute_ShouldRemoveFromCollection()
    {
        // Arrange
        var product = ProductFaker.Generate();
        product.AddAttribute("Color", "Red");

        // Act
        product.RemoveAttribute("Color");

        // Assert
        product.Attributes.ShouldBeEmpty();
    }

    #endregion
}