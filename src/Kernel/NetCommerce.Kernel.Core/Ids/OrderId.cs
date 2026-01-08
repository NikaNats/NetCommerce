namespace NetCommerce.Kernel.Core.Ids;

// record struct = Stack allocated, fast equality checks, lightweight.
public readonly record struct OrderId(Guid Value) : IStronglyTypedId<OrderId>
{
    // 1. Optimized ToString
    public override string ToString() => Value.ToString();

    // 2. Factory method required by interface (called via generics, not reflection)
    public static OrderId Create(Guid value) => new(value);

    // 3. IParsable Implementation
    public static OrderId Parse(string s, IFormatProvider? provider)
        => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out OrderId result)
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
    public int CompareTo(OrderId other) => Value.CompareTo(other.Value);
}
