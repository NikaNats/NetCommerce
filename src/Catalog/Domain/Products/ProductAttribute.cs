using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Catalog.Domain.Products;

/// <summary>
///     Dynamic product attribute for varied product types.
/// </summary>
public sealed class ProductAttribute : ValueObject
{
    internal ProductAttribute(string key, string value, string? displayName = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        DisplayName = displayName ?? key;
    }

    public string Key { get; }
    public string Value { get; }
    public string? DisplayName { get; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Key;
        yield return Value;
    }
}