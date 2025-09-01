using Catalog.Domain.Common;
using Catalog.Domain.Enums;

namespace Catalog.Domain.Entities;

/// <summary>
/// Represents an attribute definition within a ProductType.
/// This is an internal entity that defines the structure and constraints of product attributes.
/// </summary>
public class AttributeDefinition : Entity<Guid>
{
    public string Name { get; private set; }
    public DataType DataType { get; private set; }
    public bool IsRequired { get; private set; }
    public string? Description { get; private set; }
    public string? DefaultValue { get; private set; }

    public AttributeDefinition(string name, DataType dataType, bool isRequired, string? description = null, string? defaultValue = null)
        : base(Guid.NewGuid())
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Attribute name cannot be null or empty", nameof(name));

        Name = name.Trim();
        DataType = dataType;
        IsRequired = isRequired;
        Description = description?.Trim();
        DefaultValue = defaultValue?.Trim();
    }

    /// <summary>
    /// Updates the description of this attribute definition.
    /// </summary>
    /// <param name="description">The new description</param>
    public void UpdateDescription(string? description)
    {
        Description = description?.Trim();
    }

    /// <summary>
    /// Updates the default value for this attribute definition.
    /// </summary>
    /// <param name="defaultValue">The new default value</param>
    public void UpdateDefaultValue(string? defaultValue)
    {
        DefaultValue = defaultValue?.Trim();
    }

    /// <summary>
    /// Changes the required status of this attribute definition.
    /// </summary>
    /// <param name="isRequired">Whether the attribute should be required</param>
    public void ChangeRequiredStatus(bool isRequired)
    {
        IsRequired = isRequired;
    }
}