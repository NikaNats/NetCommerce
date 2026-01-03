#nullable enable
namespace NetCommerce.Catalog.Application.Categories.DTOs;

/// <summary>
///     Data transfer object for category information.
/// </summary>
public record CategoryDto(
    Guid Id,
    string Name,
    string Description,
    string Slug,
    Guid? ParentCategoryId,
    int DisplayOrder,
    bool IsActive,
    string? ImageUrl);
