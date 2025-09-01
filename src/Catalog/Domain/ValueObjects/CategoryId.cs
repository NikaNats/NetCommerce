using Catalog.Domain.Common;

namespace Catalog.Domain.ValueObjects;

/// <summary>
/// Strongly-typed identifier for Category entities.
/// </summary>
public class CategoryId : ValueObject
{
    public Guid Value { get; }

    public CategoryId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("CategoryId cannot be empty", nameof(value));
            
        Value = value;
    }

    /// <summary>
    /// Creates a new unique CategoryId.
    /// </summary>
    public static CategoryId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a CategoryId from a GUID value.
    /// </summary>
    public static CategoryId From(Guid value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();

    public static implicit operator Guid(CategoryId categoryId) => categoryId.Value;
}