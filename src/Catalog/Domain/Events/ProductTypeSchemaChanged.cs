using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Events;

/// <summary>
/// Domain event raised when a product type's schema changes (attributes added/modified).
/// This typically results in a version increment.
/// </summary>
public class ProductTypeSchemaChanged : DomainEvent
{
    public ProductTypeId ProductTypeId { get; }
    public int NewVersion { get; }

    public ProductTypeSchemaChanged(ProductTypeId productTypeId, int newVersion)
    {
        ProductTypeId = productTypeId ?? throw new ArgumentNullException(nameof(productTypeId));
        NewVersion = newVersion;
    }
}