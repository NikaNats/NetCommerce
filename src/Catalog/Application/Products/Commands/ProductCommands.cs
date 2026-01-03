#nullable enable
using NetCommerce.SharedKernel.Application;

namespace NetCommerce.Catalog.Application.Products.Commands;

/// <summary>
///     Command to create a new product.
/// </summary>
public record CreateProductCommand(
    string Name,
    string Description,
    string Sku,
    decimal Price,
    string Currency,
    Guid CategoryId) : ICommand<Guid>;

/// <summary>
///     Command to update product details.
/// </summary>
public record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string Description,
    string Sku) : ICommand;

/// <summary>
///     Command to update product price.
/// </summary>
public record UpdateProductPriceCommand(
    Guid ProductId,
    decimal NewPrice,
    string Currency) : ICommand;

/// <summary>
///     Command to publish a product.
/// </summary>
public record PublishProductCommand(Guid ProductId) : ICommand;

/// <summary>
///     Command to archive a product.
/// </summary>
public record ArchiveProductCommand(Guid ProductId) : ICommand;

/// <summary>
///     Command to add an image to a product.
/// </summary>
public record AddProductImageCommand(
    Guid ProductId,
    string ImageKey,
    int DisplayOrder,
    bool IsPrimary) : ICommand;

/// <summary>
///     Command to remove an image from a product.
/// </summary>
public record RemoveProductImageCommand(
    Guid ProductId,
    Guid ImageId) : ICommand;

/// <summary>
///     Command to add/update a product attribute.
/// </summary>
public record SetProductAttributeCommand(
    Guid ProductId,
    string Key,
    string Value,
    string? DisplayName) : ICommand;

/// <summary>
///     Command to update product SEO data.
/// </summary>
public record UpdateProductSeoCommand(
    Guid ProductId,
    string? SeoTitle,
    string? SeoDescription) : ICommand;
