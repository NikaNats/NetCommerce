using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Catalog.Domain.Categories;

/// <summary>
///     Category aggregate root for organizing products.
/// </summary>
public sealed class Category : AggregateRoot<Guid>
{
    private Category()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public Guid? ParentCategoryId { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public string? ImageKey { get; private set; }

    public static Category Create(
        string name,
        string description,
        Guid? parentCategoryId = null,
        int displayOrder = 0)
    {
        return new Category
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Slug = SlugGenerator.Generate(name),
            ParentCategoryId = parentCategoryId,
            DisplayOrder = displayOrder,
            IsActive = true
        };
    }

    public void Update(string name, string description, int displayOrder)
    {
        Name = name;
        Description = description;
        Slug = SlugGenerator.Generate(name);
        DisplayOrder = displayOrder;
    }

    public void SetParent(Guid? parentCategoryId)
    {
        ParentCategoryId = parentCategoryId;
    }

    public void SetImage(string? imageKey)
    {
        ImageKey = imageKey;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}