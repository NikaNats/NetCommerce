using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Strongly-typed identifier for Variant entities.
/// </summary>
public class VariantId : ValueObject
{
    public Guid Value { get; }

    public VariantId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("VariantId cannot be empty", nameof(value));
            
        Value = value;
    }

    /// <summary>
    /// Creates a new unique VariantId.
    /// </summary>
    public static VariantId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a VariantId from a GUID value.
    /// </summary>
    public static VariantId From(Guid value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(VariantId variantId) => variantId.Value;
}