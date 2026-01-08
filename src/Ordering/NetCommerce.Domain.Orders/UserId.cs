#nullable enable
using NetCommerce.Kernel.Core.Ids;

namespace NetCommerce.Domain.Orders;

/// <summary>
/// User-ის უნიკალური იდენტიფიკატორი.
/// </summary>
public readonly record struct UserId(Guid Value) : IStronglyTypedId<UserId>
{
    // 1. Optimized ToString
    public override string ToString() => Value.ToString();

    // 2. Factory method required by interface (called via generics, not reflection)
    public static UserId Create(Guid value) => new(value);

    // 3. IParsable Implementation
    public static UserId Parse(string s, IFormatProvider? provider)
        => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out UserId result)
    {
        if (Guid.TryParse(s, out var value))
        {
            result = new(value);
            return true;
        }

        result = default;
        return false;
    }

    // 4. IComparable Implementation (for sorting/indexing)
    public int CompareTo(UserId other) => Value.CompareTo(other.Value);
}
