using Microsoft.EntityFrameworkCore;
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Mappers;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Results;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Application.Products.Queries;

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
