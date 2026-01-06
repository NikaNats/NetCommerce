using Microsoft.EntityFrameworkCore;
using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Application.Categories.Mappers;
using NetCommerce.Catalog.Application.Categories.Queries;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Kernel.Core.Results;
using Wolverine.Attributes;

namespace NetCommerce.Catalog.Infrastructure.Handlers;

/// <summary>
///     Wolverine handler for GetAllCategoriesQuery.
/// </summary>
[WolverineHandler]
public static class GetAllCategoriesHandler
{
    public static async Task<Result<IReadOnlyList<CategoryDto>>> HandleAsync(
        GetAllCategoriesQuery query,
        CatalogDbContext db,
        ICategoryMapper mapper,
        CancellationToken cancellationToken)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Result.Success(mapper.MapToDto(categories));
    }
}

/// <summary>
///     Wolverine handler for GetCategoryByIdQuery.
/// </summary>
[WolverineHandler]
public static class GetCategoryByIdHandler
{
    public static async Task<Result<CategoryDto>> HandleAsync(
        GetCategoryByIdQuery query,
        CatalogDbContext db,
        ICategoryMapper mapper,
        CancellationToken cancellationToken)
    {
        var category = await db.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken);

        if (category is null)
            return Result.Failure<CategoryDto>(Error.NotFound("Category", query.Id));

        return mapper.MapToDto(category);
    }
}

/// <summary>
///     Wolverine handler for GetCategoryBySlugQuery.
/// </summary>
[WolverineHandler]
public static class GetCategoryBySlugHandler
{
    public static async Task<Result<CategoryDto>> HandleAsync(
        GetCategoryBySlugQuery query,
        CatalogDbContext db,
        ICategoryMapper mapper,
        CancellationToken cancellationToken)
    {
        var category = await db.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == query.Slug, cancellationToken);

        if (category is null)
            return Result.Failure<CategoryDto>(
                Error.NotFound("Category", $"slug:{query.Slug}"));

        return mapper.MapToDto(category);
    }
}

/// <summary>
///     Wolverine handler for GetRootCategoriesQuery.
/// </summary>
[WolverineHandler]
public static class GetRootCategoriesHandler
{
    public static async Task<Result<IReadOnlyList<CategoryDto>>> HandleAsync(
        GetRootCategoriesQuery query,
        CatalogDbContext db,
        ICategoryMapper mapper,
        CancellationToken cancellationToken)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.ParentCategoryId == null)
            .ToListAsync(cancellationToken);

        return Result.Success(mapper.MapToDto(categories));
    }
}

/// <summary>
///     Wolverine handler for GetChildCategoriesQuery.
/// </summary>
[WolverineHandler]
public static class GetChildCategoriesHandler
{
    public static async Task<Result<IReadOnlyList<CategoryDto>>> HandleAsync(
        GetChildCategoriesQuery query,
        CatalogDbContext db,
        ICategoryMapper mapper,
        CancellationToken cancellationToken)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.ParentCategoryId == query.ParentId)
            .ToListAsync(cancellationToken);

        return Result.Success(mapper.MapToDto(categories));
    }
}
