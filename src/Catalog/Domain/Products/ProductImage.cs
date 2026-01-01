using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Catalog.Domain.Products;

/// <summary>
///     Product image value object - stores reference to S3/CDN.
///     Only the key is stored; full URL is computed at runtime.
/// </summary>
public sealed class ProductImage : Entity<Guid>
{
    internal ProductImage(Guid id, string imageKey, int displayOrder, bool isPrimary)
    {
        Id = id;
        ImageKey = imageKey;
        DisplayOrder = displayOrder;
        IsPrimary = isPrimary;
    }

    private ProductImage()
    {
        ImageKey = string.Empty;
    }

    /// <summary>
    ///     S3 object key (e.g., "products/ps5/main.jpg").
    /// </summary>
    public string ImageKey { get; private set; }

    public int DisplayOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    internal void SetPrimary(bool isPrimary)
    {
        IsPrimary = isPrimary;
    }

    public void UpdateDisplayOrder(int order)
    {
        DisplayOrder = order;
    }
}