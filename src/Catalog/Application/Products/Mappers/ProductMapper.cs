using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.Catalog.Domain.Products;

namespace NetCommerce.Catalog.Application.Products.Mappers;

/// <summary>
/// Mapper for Product domain entities to DTOs.
/// Centralizes mapping logic following DRY and Single Responsibility principles.
/// </summary>
public sealed class ProductMapper : IProductMapper
{
    private readonly ICdnUrlGenerator _cdnUrlGenerator;

    public ProductMapper(ICdnUrlGenerator cdnUrlGenerator)
    {
        _cdnUrlGenerator = cdnUrlGenerator;
    }

    public ProductDto MapToDto(Product product)
    {
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Sku = product.Sku,
            Price = product.Price.Amount,
            Currency = product.Price.Currency,
            CategoryId = product.CategoryId,
            Status = product.Status.ToString(),
            Slug = product.Slug,
            SeoTitle = product.SeoTitle,
            SeoDescription = product.SeoDescription,
            Images = product.Images.Select(MapImageToDto).ToList(),
            Attributes = product.Attributes.Select(MapAttributeToDto).ToList()
        };
    }

    public ProductListItemDto MapToListItemDto(Product product)
    {
        return new ProductListItemDto
        {
            Id = product.Id,
            Name = product.Name,
            Sku = product.Sku,
            Price = product.Price.Amount,
            Currency = product.Price.Currency,
            PrimaryImageUrl = product.Images
                .Where(i => i.IsPrimary)
                .Select(i => _cdnUrlGenerator.GenerateUrl(i.ImageKey))
                .FirstOrDefault(),
            Status = product.Status.ToString(),
            Slug = product.Slug
        };
    }

    public IReadOnlyList<ProductDto> MapToDto(IEnumerable<Product> products)
    {
        return products.Select(MapToDto).ToList().AsReadOnly();
    }

    public IReadOnlyList<ProductListItemDto> MapToListItemDto(IEnumerable<Product> products)
    {
        return products.Select(MapToListItemDto).ToList().AsReadOnly();
    }

    private ProductImageDto MapImageToDto(ProductImage image)
    {
        return new ProductImageDto
        {
            Id = image.Id,
            Url = _cdnUrlGenerator.GenerateUrl(image.ImageKey),
            DisplayOrder = image.DisplayOrder,
            IsPrimary = image.IsPrimary
        };
    }

    private static ProductAttributeDto MapAttributeToDto(ProductAttribute attribute)
    {
        return new ProductAttributeDto
        {
            Key = attribute.Key,
            Value = attribute.Value,
            DisplayName = attribute.DisplayName ?? attribute.Key
        };
    }
}

/// <summary>
/// Interface for product mapping operations.
/// Supports Dependency Inversion Principle.
/// </summary>
public interface IProductMapper
{
    ProductDto MapToDto(Product product);
    ProductListItemDto MapToListItemDto(Product product);
    IReadOnlyList<ProductDto> MapToDto(IEnumerable<Product> products);
    IReadOnlyList<ProductListItemDto> MapToListItemDto(IEnumerable<Product> products);
}
