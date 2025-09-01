using Catalog.Domain.Common;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Aggregates;

/// <summary>
/// Category aggregate root representing a product category for organization and navigation.
/// Categories can form hierarchical structures (parent-child relationships).
/// </summary>
public class Category : AggregateRoot<CategoryId>
{
    // State - encapsulated with private setters
    public string Name { get; private set; }
    public string Description { get; private set; }
    public CategoryId? ParentCategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    // Private constructor for entity framework and deserialization
    private Category(CategoryId id, string name) : base(id)
    {
        Name = name;
        Description = string.Empty;
        IsActive = true;
        SortOrder = 0;
    }

    /// <summary>
    /// Factory method to create a new category.
    /// </summary>
    /// <param name="id">The category identifier</param>
    /// <param name="name">The category name</param>
    /// <param name="description">Optional description</param>
    /// <param name="parentCategoryId">Optional parent category for hierarchical structure</param>
    /// <returns>A new Category instance</returns>
    public static Category Create(CategoryId id, string name, string? description = null, CategoryId? parentCategoryId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Category name is required.");

        var category = new Category(id, name.Trim())
        {
            Description = description?.Trim() ?? string.Empty,
            ParentCategoryId = parentCategoryId
        };

        return category;
    }

    /// <summary>
    /// Updates the category name.
    /// </summary>
    /// <param name="name">The new category name</param>
    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Category name is required.");

        Name = name.Trim();
    }

    /// <summary>
    /// Updates the category description.
    /// </summary>
    /// <param name="description">The new description</param>
    public void UpdateDescription(string? description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Sets the parent category for hierarchical organization.
    /// </summary>
    /// <param name="parentCategoryId">The parent category identifier</param>
    public void SetParentCategory(CategoryId parentCategoryId)
    {
        if (parentCategoryId?.Value == Id.Value)
            throw new BusinessRuleException("A category cannot be its own parent.");

        ParentCategoryId = parentCategoryId;
    }

    /// <summary>
    /// Removes the parent category, making this a root-level category.
    /// </summary>
    public void RemoveParentCategory()
    {
        ParentCategoryId = null;
    }

    /// <summary>
    /// Activates the category, making it visible in the catalog.
    /// </summary>
    public void Activate()
    {
        IsActive = true;
    }

    /// <summary>
    /// Deactivates the category, hiding it from the catalog.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Sets the sort order for category display ordering.
    /// </summary>
    /// <param name="sortOrder">The sort order (lower numbers appear first)</param>
    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    /// <summary>
    /// Checks if this category is a root category (has no parent).
    /// </summary>
    /// <returns>True if this is a root category</returns>
    public bool IsRootCategory()
    {
        return ParentCategoryId == null;
    }
}