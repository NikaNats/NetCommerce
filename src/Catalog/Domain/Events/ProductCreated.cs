using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Events;

/// <summary>
/// Domain event raised when a product is created.
/// </summary>
public class ProductCreated : DomainEvent
{
    public ProductId ProductId { get; }

    public ProductCreated(ProductId productId)
    {
        ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
    }
}