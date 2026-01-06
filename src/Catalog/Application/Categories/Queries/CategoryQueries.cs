using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Catalog.Application.Categories.Queries;

/// <summary>
///     Query to get all categories.
/// </summary>
public record GetAllCategoriesQuery : IQuery<IReadOnlyList<CategoryDto>>;

/// <summary>
///     Query to get a category by ID.
/// </summary>
public record GetCategoryByIdQuery(Guid Id) : IQuery<CategoryDto>;

/// <summary>
///     Query to get a category by slug.
/// </summary>
public record GetCategoryBySlugQuery(string Slug) : IQuery<CategoryDto>;

/// <summary>
///     Query to get child categories of a parent.
/// </summary>
public record GetChildCategoriesQuery(Guid ParentId) : IQuery<IReadOnlyList<CategoryDto>>;

/// <summary>
///     Query to get root categories (no parent).
/// </summary>
public record GetRootCategoriesQuery : IQuery<IReadOnlyList<CategoryDto>>;