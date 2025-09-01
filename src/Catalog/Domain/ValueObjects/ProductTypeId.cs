using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Strongly-typed identifier for ProductType entities.
/// </summary>
public class ProductTypeId : ValueObject
{
    public Guid Value { get; }

    public ProductTypeId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("ProductTypeId cannot be empty", nameof(value));
            
        Value = value;
    }

    /// <summary>
    /// Creates a new unique ProductTypeId.
    /// </summary>
    public static ProductTypeId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a ProductTypeId from a GUID value.
    /// </summary>
    public static ProductTypeId From(Guid value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(ProductTypeId productTypeId) => productTypeId.Value;
}