using Catalog.Domain.Common;
using Catalog.Domain.Entities;
using Catalog.Domain.Enums;
using Catalog.Domain.Events;
using Catalog.Domain.ValueObjects;

namespace Catalog.Domain.Aggregates;

/// <summary>
/// ProductType aggregate root that defines the structure, attributes, and validation rules for products.
/// Manages schema evolution through versioning and attribute definitions.
/// </summary>
public class ProductType : AggregateRoot<ProductTypeId>
{
    // State - encapsulated with private setters
    public string Name { get; private set; }
    public int Version { get; private set; }
    public string? Description { get; private set; }
    
    private readonly List<AttributeDefinition> _attributeDefinitions;

    /// <summary>
    /// Gets a read-only collection of attribute definitions for this product type.
    /// </summary>
    public IReadOnlyList<AttributeDefinition> AttributeDefinitions => _attributeDefinitions.AsReadOnly();

    // Private constructor for entity framework and deserialization
    private ProductType(ProductTypeId id, string name) : base(id)
    {
        Name = name;
        Version = 1; // Start with version 1
        Description = string.Empty;
        _attributeDefinitions = [];
    }

    /// <summary>
    /// Factory method to create a new product type.
    /// </summary>
    /// <param name="id">The product type identifier</param>
    /// <param name="name">The name of the product type</param>
    /// <param name="description">Optional description</param>
    /// <returns>A new ProductType instance</returns>
    public static ProductType Create(ProductTypeId id, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Product type name is required.");

        var productType = new ProductType(id, name.Trim())
        {
            Description = description?.Trim() ?? string.Empty
        };

        return productType;
    }

    /// <summary>
    /// Updates the description of this product type.
    /// </summary>
    /// <param name="description">The new description</param>
    public void UpdateDescription(string? description)
    {
        Description = description?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Adds a new attribute definition to this product type.
    /// This is a structural change that increments the version.
    /// </summary>
    /// <param name="name">The attribute name</param>
    /// <param name="dataType">The data type of the attribute</param>
    /// <param name="isRequired">Whether the attribute is required</param>
    /// <param name="description">Optional description of the attribute</param>
    /// <param name="defaultValue">Optional default value</param>
    public void AddNewAttribute(string name, DataType dataType, bool isRequired, string? description = null, string? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Attribute name is required.");

        // Check if attribute already exists
        if (_attributeDefinitions.Any(ad => ad.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new BusinessRuleException($"Attribute '{name}' already exists for this product type.");

        var newAttributeDefinition = new AttributeDefinition(name, dataType, isRequired, description, defaultValue);
        _attributeDefinitions.Add(newAttributeDefinition);

        // Structural change increments version
        Version++;
        AddDomainEvent(new ProductTypeSchemaChanged(Id, Version));
    }

    /// <summary>
    /// Removes an attribute definition from this product type.
    /// This is a structural change that increments the version.
    /// </summary>
    /// <param name="attributeName">The name of the attribute to remove</param>
    public void RemoveAttribute(string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return;

        var attributeToRemove = _attributeDefinitions
            .FirstOrDefault(ad => ad.Name.Equals(attributeName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (attributeToRemove == null)
            return; // Attribute doesn't exist, no-op

        _attributeDefinitions.Remove(attributeToRemove);

        // Structural change increments version
        Version++;
        AddDomainEvent(new ProductTypeSchemaChanged(Id, Version));
    }

    /// <summary>
    /// Updates an existing attribute definition.
    /// Non-structural changes (like description) don't increment version,
    /// but structural changes (like making required/optional) do.
    /// </summary>
    /// <param name="attributeName">The name of the attribute to update</param>
    /// <param name="isRequired">New required status</param>
    /// <param name="description">New description</param>
    /// <param name="defaultValue">New default value</param>
    public void UpdateAttribute(string attributeName, bool? isRequired = null, string? description = null, string? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return;

        var attribute = _attributeDefinitions
            .FirstOrDefault(ad => ad.Name.Equals(attributeName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (attribute == null)
            throw new BusinessRuleException($"Attribute '{attributeName}' does not exist in this product type.");

        bool structuralChange = false;

        // Check for structural changes
        if (isRequired.HasValue && attribute.IsRequired != isRequired.Value)
        {
            attribute.ChangeRequiredStatus(isRequired.Value);
            structuralChange = true;
        }

        // Non-structural changes
        if (description != null)
        {
            attribute.UpdateDescription(description);
        }

        if (defaultValue != null)
        {
            attribute.UpdateDefaultValue(defaultValue);
        }

        // Only increment version for structural changes
        if (structuralChange)
        {
            Version++;
            AddDomainEvent(new ProductTypeSchemaChanged(Id, Version));
        }
    }

    /// <summary>
    /// Gets an attribute definition by name.
    /// </summary>
    /// <param name="attributeName">The name of the attribute</param>
    /// <returns>The attribute definition if found, null otherwise</returns>
    public AttributeDefinition? GetAttributeDefinition(string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
            return null;

        return _attributeDefinitions
            .FirstOrDefault(ad => ad.Name.Equals(attributeName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if this product type has a specific attribute definition.
    /// </summary>
    /// <param name="attributeName">The name of the attribute</param>
    /// <returns>True if the attribute exists, false otherwise</returns>
    public bool HasAttribute(string attributeName)
    {
        return GetAttributeDefinition(attributeName) != null;
    }

    /// <summary>
    /// Gets all required attribute definitions.
    /// </summary>
    /// <returns>A list of required attribute definitions</returns>
    public List<AttributeDefinition> GetRequiredAttributes()
    {
        return _attributeDefinitions.Where(ad => ad.IsRequired).ToList();
    }

    /// <summary>
    /// Validates that a set of attributes conforms to this product type's schema.
    /// </summary>
    /// <param name="attributes">The attributes to validate</param>
    /// <returns>A list of validation errors, empty if valid</returns>
    public List<string> ValidateAttributes(List<Catalog.Domain.ValueObjects.Attribute> attributes)
    {
        var errors = new List<string>();
        var attributeDict = attributes?.ToDictionary(a => a.Name, a => a.Value) ?? new Dictionary<string, string>();

        // Check required attributes
        foreach (var requiredAttribute in GetRequiredAttributes())
        {
            if (!attributeDict.ContainsKey(requiredAttribute.Name) || 
                string.IsNullOrWhiteSpace(attributeDict[requiredAttribute.Name]))
            {
                errors.Add($"Required attribute '{requiredAttribute.Name}' is missing or empty.");
            }
        }

        // Check for unknown attributes
        foreach (var providedAttribute in attributeDict.Keys)
        {
            if (!HasAttribute(providedAttribute))
            {
                errors.Add($"Unknown attribute '{providedAttribute}' is not defined for this product type.");
            }
        }

        return errors;
    }
}