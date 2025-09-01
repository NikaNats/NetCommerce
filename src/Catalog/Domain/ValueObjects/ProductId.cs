using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Strongly-typed identifier for Product entities.
/// Provides type safety and prevents mixing up different types of IDs.
/// </summary>
public class ProductId : ValueObject
{
    public Guid Value { get; }

    public ProductId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("ProductId cannot be empty", nameof(value));
            
        Value = value;
    }

    /// <summary>
    /// Creates a new unique ProductId.
    /// </summary>
    /// <returns>A new ProductId with a unique GUID value</returns>
    public static ProductId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a ProductId from a GUID value.
    /// </summary>
    /// <param name="value">The GUID value</param>
    /// <returns>A ProductId with the specified value</returns>
    public static ProductId From(Guid value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(ProductId productId) => productId.Value;
}