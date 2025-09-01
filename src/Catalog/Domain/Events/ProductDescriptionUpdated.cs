using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Events;

/// <summary>
/// Domain event raised when a product description is updated.
/// </summary>
public class ProductDescriptionUpdated : DomainEvent
{
    public ProductId ProductId { get; }

    public ProductDescriptionUpdated(ProductId productId)
    {
        ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
    }
}