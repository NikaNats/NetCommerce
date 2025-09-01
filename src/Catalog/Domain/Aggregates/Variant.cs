using Catalog.Domain.Common;
using Catalog.Domain.Events;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Aggregates;

/// <summary>
/// Variant aggregate root representing a sellable unit (SKU) of a product.
/// Manages pricing, defining attributes, and variant-specific business logic.
/// </summary>
public class Variant : AggregateRoot<VariantId>
{
    // State - encapsulated with private setters
    public ProductId ProductId { get; private set; }
    public SKU Sku { get; private set; }
    public Price Price { get; private set; }
    
    private readonly List<Catalog.Domain.ValueObjects.Attribute> _definingAttributes;

    /// <summary>
    /// Gets a read-only collection of defining attributes that distinguish this variant.
    /// </summary>
    public IReadOnlyList<Catalog.Domain.ValueObjects.Attribute> DefiningAttributes => _definingAttributes.AsReadOnly();

    // Private constructor for entity framework and deserialization
    private Variant(VariantId id, ProductId productId, SKU sku) : base(id)
    {
        ProductId = productId;
        Sku = sku;
        Price = ValueObjects.Price.Zero; // Safe default value
        _definingAttributes = [];
    }

    /// <summary>
    /// Factory method to create a variant from an existing SKU.
    /// This method is typically called when a SkuWasRegistered event is received.
    /// </summary>
    /// <param name="id">The variant identifier</param>
    /// <param name="productId">The product this variant belongs to</param>
    /// <param name="sku">The SKU for this variant</param>
    /// <returns>A new Variant instance</returns>
    public static Variant CreateFromSku(VariantId id, ProductId productId, SKU sku)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        if (productId == null) throw new ArgumentNullException(nameof(productId));
        if (sku == null) throw new ArgumentNullException(nameof(sku));

        var variant = new Variant(id, productId, sku);
        variant.AddDomainEvent(new VariantCreated(id, productId, sku));
        
        return variant;
    }

    /// <summary>
    /// Updates the price of this variant.
    /// Business rule: Only changes price if the new price is different from current.
    /// </summary>
    /// <param name="newPrice">The new price for this variant</param>
    public void UpdatePrice(Price newPrice)
    {
        if (newPrice == null)
            throw new ArgumentNullException(nameof(newPrice));

        if (Price.Equals(newPrice))
            return; // No change, avoid unnecessary events

        Price = newPrice;
        AddDomainEvent(new VariantPriceChanged(Id, newPrice));
    }

    /// <summary>
    /// Sets the defining attributes that distinguish this variant from other variants of the same product.
    /// For example: Color=Red, Size=Large for a t-shirt variant.
    /// </summary>
    /// <param name="attributes">The list of defining attributes</param>
    public void DefineAttributes(List<Catalog.Domain.ValueObjects.Attribute> attributes)
    {
        if (attributes == null)
            throw new ArgumentNullException(nameof(attributes));

        _definingAttributes.Clear();
        _definingAttributes.AddRange(attributes.Where(a => a != null));
    }

    /// <summary>
    /// Adds or updates a single defining attribute.
    /// </summary>
    /// <param name="attribute">The attribute to add or update</param>
    public void SetDefiningAttribute(Catalog.Domain.ValueObjects.Attribute attribute)
    {
        if (attribute == null)
            throw new ArgumentNullException(nameof(attribute));

        // Remove existing attribute with the same name
        _definingAttributes.RemoveAll(a => a.Name == attribute.Name);
        
        // Add the new/updated attribute
        _definingAttributes.Add(attribute);
    }

    /// <summary>
    /// Removes a defining attribute.
    /// </summary>
    /// <param name="attributeName">The name of the attribute to remove</param>
    public void RemoveDefiningAttribute(string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return;

        _definingAttributes.RemoveAll(a => a.Name == attributeName.Trim());
    }

    /// <summary>
    /// Gets a defining attribute by name.
    /// </summary>
    /// <param name="attributeName">The name of the attribute</param>
    /// <returns>The attribute if found, null otherwise</returns>
    public Catalog.Domain.ValueObjects.Attribute? GetDefiningAttribute(string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return null;

        return _definingAttributes.FirstOrDefault(a => a.Name == attributeName.Trim());
    }

    /// <summary>
    /// Checks if this variant has a specific defining attribute.
    /// </summary>
    /// <param name="attributeName">The name of the attribute to check</param>
    /// <returns>True if the attribute exists, false otherwise</returns>
    public bool HasDefiningAttribute(string attributeName)
    {
        return GetDefiningAttribute(attributeName) != null;
    }
}