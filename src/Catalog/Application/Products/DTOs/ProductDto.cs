namespace NetCommerce.Catalog.Application.Products.DTOs;

/// <summary>
///     Product response DTO.
/// </summary>
public record ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = string.Empty;
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? SeoTitle { get; init; }
    public string? SeoDescription { get; init; }
    public IReadOnlyList<ProductImageDto> Images { get; init; } = [];
    public IReadOnlyList<ProductAttributeDto> Attributes { get; init; } = [];
}

public record ProductImageDto
{
    public Guid Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public bool IsPrimary { get; init; }
}

public record ProductAttributeDto
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public record ProductListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? PrimaryImageUrl { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Slug { get; init; }
}