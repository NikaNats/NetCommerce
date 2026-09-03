using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Catalog.Domain.Products;

/// <summary>
///     Product aggregate root - the main entity in the Catalog bounded context.
/// </summary>
public sealed class Product : AggregateRoot<Guid>
{
    private readonly List<ProductAttribute> _attributes = [];
    private readonly List<ProductImage> _images = [];

    private Product()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public Money Price { get; private set; } = default!;
    public decimal WeightKg { get; private set; }
    public Guid CategoryId { get; private set; }
    public ProductStatus Status { get; private set; }
    public string? SeoTitle { get; private set; }
    public string? SeoDescription { get; private set; }
    public string? Slug { get; private set; }

    public IReadOnlyList<ProductImage> Images => _images.AsReadOnly();
    public IReadOnlyList<ProductAttribute> Attributes => _attributes.AsReadOnly();

    public static Product Create(
        string name,
        string description,
        string sku,
        Money price,
        Guid categoryId)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Sku = sku,
            Price = price,
            CategoryId = categoryId,
            Status = ProductStatus.Draft,
            Slug = SlugGenerator.Generate(name)
        };

        product.RaiseDomainEvent(new ProductCreatedDomainEvent(product.Id, product.Name, product.Sku));

        return product;
    }

    public void UpdateDetails(string name, string description, string sku)
    {
        var oldSku = Sku;
        var oldSlug = Slug;

        Name = name;
        Description = description;
        Sku = sku;
        Slug = SlugGenerator.Generate(name);

        RaiseDomainEvent(new ProductUpdatedDomainEvent(Id, Name, oldSku, Sku, oldSlug, Slug));
    }

    public void UpdatePrice(Money newPrice)
    {
        var oldPrice = Price;
        Price = newPrice;

        RaiseDomainEvent(new ProductPriceChangedDomainEvent(Id, Sku, Slug, oldPrice, newPrice));
    }

    public void AssignCategory(Guid categoryId)
    {
        CategoryId = categoryId;
    }

    public void UpdateSeo(string? seoTitle, string? seoDescription)
    {
        SeoTitle = seoTitle;
        SeoDescription = seoDescription;
    }

    public void Publish()
    {
        if (Status == ProductStatus.Published)
            throw new InvalidOperationException("Product is already published");

        Status = ProductStatus.Published;
        RaiseDomainEvent(new ProductPublishedDomainEvent(Id));
    }

    public void Archive()
    {
        Status = ProductStatus.Archived;
        RaiseDomainEvent(new ProductArchivedDomainEvent(Id));
    }

    public void AddImage(string imageKey, int displayOrder, bool isPrimary = false)
    {
        if (isPrimary)
            // Remove primary flag from existing images
            foreach (var img in _images.Where(i => i.IsPrimary))
                img.SetPrimary(false);

        var image = new ProductImage(Guid.NewGuid(), imageKey, displayOrder, isPrimary);
        _images.Add(image);
    }

    public void RemoveImage(Guid imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image != null) _images.Remove(image);
    }

    public void AddAttribute(string key, string value, string? displayName = null)
    {
        var attribute = new ProductAttribute(key, value, displayName);
        _attributes.Add(attribute);
    }

    public void RemoveAttribute(string key)
    {
        var attribute = _attributes.FirstOrDefault(a => a.Key == key);
        if (attribute != null) _attributes.Remove(attribute);
    }

    /// <summary>
    ///     Rehydrates a <see cref="Product"/> from a snapshot (e.g., cache deserialization).
    ///     Deliberately raises NO domain events: a cache hit is not a business fact.
    ///     Internal by design — only persistence and caching infrastructure (friend
    ///     assemblies) may bypass the creation invariants enforced by
    ///     <see cref="Create"/>.
    /// </summary>
    internal static Product Rehydrate(
        Guid id,
        string name,
        string description,
        string sku,
        Money price,
        decimal weightKg,
        Guid categoryId,
        ProductStatus status,
        string? seoTitle,
        string? seoDescription,
        string? slug,
        IReadOnlyList<(Guid Id, string ImageKey, int DisplayOrder, bool IsPrimary)> images,
        IReadOnlyList<(string Key, string Value, string? DisplayName)> attributes)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            Description = description,
            Sku = sku,
            Price = price,
            WeightKg = weightKg,
            CategoryId = categoryId,
            Status = status,
            SeoTitle = seoTitle,
            SeoDescription = seoDescription,
            Slug = slug
        };

        foreach (var image in images)
            product._images.Add(new ProductImage(image.Id, image.ImageKey, image.DisplayOrder, image.IsPrimary));

        foreach (var attribute in attributes)
            product._attributes.Add(new ProductAttribute(attribute.Key, attribute.Value, attribute.DisplayName));

        return product;
    }
}

public enum ProductStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}
