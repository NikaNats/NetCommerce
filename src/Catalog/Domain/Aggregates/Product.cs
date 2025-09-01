using Catalog.Domain.Common;
using Catalog.Domain.Enums;
using Catalog.Domain.Events;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Aggregates;

/// <summary>
/// Product aggregate root representing a product in the catalog.
/// Manages product lifecycle, descriptive attributes, and business rules.
/// </summary>
public class Product : AggregateRoot<ProductId>
{
    // State - encapsulated with private setters
    public string Name { get; private set; }
    public string Description { get; private set; }
    public ProductStatus Status { get; private set; }
    public ProductTypeId TypeId { get; private set; }
    public int ProductTypeVersion { get; private set; }
    public BrandId? BrandId { get; private set; }
    
    private readonly List<Catalog.Domain.ValueObjects.Attribute> _descriptiveAttributes;

    /// <summary>
    /// Gets a read-only collection of descriptive attributes for this product.
    /// </summary>
    public IReadOnlyList<Catalog.Domain.ValueObjects.Attribute> DescriptiveAttributes => _descriptiveAttributes.AsReadOnly();

    // Private constructor for entity framework and deserialization
    private Product(ProductId id, string name, ProductTypeId typeId, int version) : base(id)
    {
        Name = name;
        Status = ProductStatus.Draft;
        TypeId = typeId;
        ProductTypeVersion = version;
        Description = string.Empty;
        _descriptiveAttributes = [];
    }

    /// <summary>
    /// Factory method to create a new product in draft status.
    /// Uses the Factory Method pattern to ensure valid initial state.
    /// </summary>
    /// <param name="id">The product identifier</param>
    /// <param name="name">The product name</param>
    /// <param name="typeId">The product type identifier</param>
    /// <param name="version">The product type version</param>
    /// <returns>A new Product instance in Draft status</returns>
    public static Product CreateDraft(ProductId id, string name, ProductTypeId typeId, int version)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Product name is required.");

        var product = new Product(id, name.Trim(), typeId, version);
        product.AddDomainEvent(new ProductCreated(id));
        
        return product;
    }

    /// <summary>
    /// Publishes the product, making it available for sale.
    /// Business rule: A product must have at least one active variant to be published.
    /// </summary>
    /// <param name="hasActiveVariants">Whether the product has at least one active variant</param>
    public void Publish(bool hasActiveVariants)
    {
        if (!hasActiveVariants)
            throw new BusinessRuleException("A product must have at least one active variant to be published.");

        if (Status != ProductStatus.Draft)
            throw new BusinessRuleException("Only a draft product can be published.");

        Status = ProductStatus.Published;
        AddDomainEvent(new ProductPublished(Id));
    }

    /// <summary>
    /// Archives the product, removing it from active catalog.
    /// </summary>
    public void Archive()
    {
        if (Status == ProductStatus.Archived)
            return; // Already archived, no-op

        Status = ProductStatus.Archived;
        // Could add ProductArchived event if needed
    }

    /// <summary>
    /// Changes the product description.
    /// </summary>
    /// <param name="newDescription">The new description</param>
    public void ChangeDescription(string? newDescription)
    {
        var description = newDescription?.Trim() ?? string.Empty;
        
        if (Description == description)
            return; // No change, avoid unnecessary event

        Description = description;
        AddDomainEvent(new ProductDescriptionUpdated(Id));
    }

    /// <summary>
    /// Associates the product with a brand.
    /// </summary>
    /// <param name="brandId">The brand identifier</param>
    public void AssignToBrand(BrandId brandId)
    {
        BrandId = brandId ?? throw new ArgumentNullException(nameof(brandId));
    }

    /// <summary>
    /// Removes the product from its current brand association.
    /// </summary>
    public void RemoveFromBrand()
    {
        BrandId = null;
    }

    /// <summary>
    /// Updates the product to comply with a new version of its ProductType.
    /// This method handles schema evolution and attribute updates.
    /// </summary>
    /// <param name="newVersion">The new ProductType version</param>
    /// <param name="attributesToAdd">New attributes to add based on the updated schema</param>
    public void UpdateCompliance(int newVersion, List<Catalog.Domain.ValueObjects.Attribute> attributesToAdd)
    {
        if (newVersion <= ProductTypeVersion)
            throw new BusinessRuleException("New version must be higher than current version.");

        ProductTypeVersion = newVersion;

        if (attributesToAdd?.Any() == true)
        {
            _descriptiveAttributes.AddRange(attributesToAdd);
        }

        // Could add ProductComplianceUpdated event if needed
    }

    /// <summary>
    /// Adds or updates a descriptive attribute.
    /// </summary>
    /// <param name="attribute">The attribute to add or update</param>
    public void SetDescriptiveAttribute(Catalog.Domain.ValueObjects.Attribute attribute)
    {
        if (attribute == null)
            throw new ArgumentNullException(nameof(attribute));

        // Remove existing attribute with the same name
        _descriptiveAttributes.RemoveAll(a => a.Name == attribute.Name);
        
        // Add the new/updated attribute
        _descriptiveAttributes.Add(attribute);
    }

    /// <summary>
    /// Removes a descriptive attribute.
    /// </summary>
    /// <param name="attributeName">The name of the attribute to remove</param>
    public void RemoveDescriptiveAttribute(string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return;

        _descriptiveAttributes.RemoveAll(a => a.Name == attributeName.Trim());
    }
}