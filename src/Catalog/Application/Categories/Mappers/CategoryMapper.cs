using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.Catalog.Domain.Categories;

namespace NetCommerce.Catalog.Application.Categories.Mappers;

/// <summary>
///     Mapper for Category domain entities to DTOs.
///     Centralizes mapping logic following DRY and Single Responsibility principles.
/// </summary>
public sealed class CategoryMapper : ICategoryMapper
{
    private readonly ICdnUrlGenerator _cdnUrlGenerator;

    public CategoryMapper(ICdnUrlGenerator cdnUrlGenerator)
    {
        _cdnUrlGenerator = cdnUrlGenerator;
    }

    public CategoryDto MapToDto(Category category)
    {
        return new CategoryDto(
            category.Id,
            category.Name,
            category.Description,
            category.Slug,
            category.ParentCategoryId,
            category.DisplayOrder,
            category.IsActive,
            category.ImageKey != null ? _cdnUrlGenerator.GenerateUrl(category.ImageKey) : null);
    }

    public IReadOnlyList<CategoryDto> MapToDto(IEnumerable<Category> categories)
    {
        return categories.Select(MapToDto).ToList().AsReadOnly();
    }
}

/// <summary>
///     Interface for category mapping operations.
///     Supports Dependency Inversion Principle.
/// </summary>
public interface ICategoryMapper
{
    CategoryDto MapToDto(Category category);
    IReadOnlyList<CategoryDto> MapToDto(IEnumerable<Category> categories);
}