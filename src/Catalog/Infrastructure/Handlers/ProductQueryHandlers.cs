using Microsoft.EntityFrameworkCore;
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Mappers;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Results;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Infrastructure.Handlers;

/// <summary>
///     Wolverine handler for GetProductByIdQuery.
/// </summary>
[WolverineHandler]
public static class GetProductByIdHandler
{
    public static async Task<Result<ProductDto>> HandleAsync(
        GetProductByIdQuery query,
        CatalogDbContext db,
        IProductMapper mapper,
        CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.ProductId, cancellationToken);

        if (product is null)
            return Result.Failure<ProductDto>(
                Error.NotFound(nameof(Product), query.ProductId));

        return mapper.MapToDto(product);
    }
}

/// <summary>
///     Wolverine handler for GetProductBySlugQuery.
/// </summary>
[WolverineHandler]
public static class GetProductBySlugHandler
{
    public static async Task<Result<ProductDto>> HandleAsync(
        GetProductBySlugQuery query,
        CatalogDbContext db,
        IProductMapper mapper,
        CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == query.Slug, cancellationToken);

        if (product is null)
            return Result.Failure<ProductDto>(
                Error.NotFound(nameof(Product), query.Slug));

        return mapper.MapToDto(product);
    }
}

/// <summary>
///     Wolverine handler for SearchProductsQuery.
///     Serves the public catalog listing with optional text/category/price
///     filters and server-side pagination.
/// </summary>
[WolverineHandler]
public static class SearchProductsHandler
{
    public static async Task<Result<PagedResult<ProductListItemDto>>> HandleAsync(
        SearchProductsQuery query,
        CatalogDbContext db,
        IProductMapper mapper,
        CancellationToken cancellationToken)
    {
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var filtered = db.Products.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            filtered = filtered.Where(p =>
                p.Name.Contains(term) ||
                p.Description.Contains(term) ||
                p.Sku.Contains(term));
        }

        if (query.CategoryId.HasValue)
            filtered = filtered.Where(p => p.CategoryId == query.CategoryId.Value);

        if (query.MinPrice.HasValue)
            filtered = filtered.Where(p => p.Price.Amount >= query.MinPrice.Value);

        if (query.MaxPrice.HasValue)
            filtered = filtered.Where(p => p.Price.Amount <= query.MaxPrice.Value);

        var totalCount = await filtered.CountAsync(cancellationToken);

        var products = await filtered
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = mapper.MapToListItemDto(products);

        return Result.Success(PagedResult<ProductListItemDto>.Create(
            items,
            totalCount,
            pageNumber,
            pageSize));
    }
}
