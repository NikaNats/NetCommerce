using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Strongly-typed identifier for Brand entities.
/// </summary>
public class BrandId : ValueObject
{
    public Guid Value { get; }

    public BrandId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("BrandId cannot be empty", nameof(value));
            
        Value = value;
    }

    /// <summary>
    /// Creates a new unique BrandId.
    /// </summary>
    public static BrandId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a BrandId from a GUID value.
    /// </summary>
    public static BrandId From(Guid value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(BrandId brandId) => brandId.Value;
}