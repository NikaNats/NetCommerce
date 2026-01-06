#nullable enable
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Application;

namespace NetCommerce.Catalog.Application.Products.Queries;

/// <summary>
///     Query to get a product by ID.
/// </summary>
public record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto>;

/// <summary>
///     Query to get a product by slug.
/// </summary>
public record GetProductBySlugQuery(string Slug) : IQuery<ProductDto>;

/// <summary>
///     Query to search products with pagination.
/// </summary>
public record SearchProductsQuery(
    string? SearchTerm,
    Guid? CategoryId,
    decimal? MinPrice,
    decimal? MaxPrice,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PagedResult<ProductListItemDto>>;

/// <summary>
///     Query to get products by category.
/// </summary>
public record GetProductsByCategoryQuery(
    Guid CategoryId,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PagedResult<ProductListItemDto>>;
