using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Events;

/// <summary>
/// Domain event raised when a variant is created.
/// </summary>
public class VariantCreated : DomainEvent
{
    public VariantId VariantId { get; }
    public ProductId ProductId { get; }
    public SKU Sku { get; }

    public VariantCreated(VariantId variantId, ProductId productId, SKU sku)
    {
        VariantId = variantId ?? throw new ArgumentNullException(nameof(variantId));
        ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
        Sku = sku ?? throw new ArgumentNullException(nameof(sku));
    }
}