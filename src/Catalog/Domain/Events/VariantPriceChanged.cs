using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Events;

/// <summary>
/// Domain event raised when a variant's price is changed.
/// </summary>
public class VariantPriceChanged : DomainEvent
{
    public VariantId VariantId { get; }
    public Price NewPrice { get; }

    public VariantPriceChanged(VariantId variantId, Price newPrice)
    {
        VariantId = variantId ?? throw new ArgumentNullException(nameof(variantId));
        NewPrice = newPrice ?? throw new ArgumentNullException(nameof(newPrice));
    }
}