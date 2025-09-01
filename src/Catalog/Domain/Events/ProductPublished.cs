using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Events;

/// <summary>
/// Domain event raised when a product is published.
/// </summary>
public class ProductPublished : DomainEvent
{
    public ProductId ProductId { get; }

    public ProductPublished(ProductId productId)
    {
        ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
    }
}