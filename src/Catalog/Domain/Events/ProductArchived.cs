using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Events;

/// <summary>
/// Domain event raised when a product is archived.
/// </summary>
public class ProductArchived : DomainEvent
{
    public ProductId ProductId { get; }

    public ProductArchived(ProductId productId)
    {
        ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
    }
}