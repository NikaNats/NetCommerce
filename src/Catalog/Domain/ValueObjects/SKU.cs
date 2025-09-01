using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Represents a Stock Keeping Unit (SKU) identifier.
/// This value object ensures SKU format validation and immutability.
/// </summary>
public class SKU : ValueObject
{
    public string Value { get; }

    public SKU(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SKU cannot be null or empty", nameof(value));

        // Basic validation - SKU should be alphanumeric with hyphens/underscores allowed
        var cleanValue = value.Trim().ToUpperInvariant();
        if (!IsValidSku(cleanValue))
            throw new ArgumentException("SKU contains invalid characters. Only letters, numbers, hyphens, and underscores are allowed", nameof(value));

        Value = cleanValue;
    }

    /// <summary>
    /// Creates a SKU from a string value.
    /// </summary>
    /// <param name="value">The SKU string value</param>
    /// <returns>A new SKU instance</returns>
    public static SKU From(string value) => new(value);

    private static bool IsValidSku(string sku)
    {
        if (string.IsNullOrEmpty(sku) || sku.Length > 50)
            return false;

        return sku.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(SKU sku) => sku.Value;
}