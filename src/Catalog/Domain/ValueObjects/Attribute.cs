using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Represents a product attribute with name and value.
/// This value object is immutable and represents descriptive or defining characteristics.
/// </summary>
public class Attribute : ValueObject
{
    public string Name { get; }
    public string Value { get; }

    public Attribute(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Attribute name cannot be null or empty", nameof(name));

        Name = name.Trim();
        Value = value?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Creates a new attribute with the specified name and value.
    /// </summary>
    /// <param name="name">The attribute name</param>
    /// <param name="value">The attribute value</param>
    /// <returns>A new Attribute instance</returns>
    public static Attribute Create(string name, string value) => new(name, value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return Value;
    }

    public override string ToString() => $"{Name}: {Value}";
}