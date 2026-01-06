#nullable enable
namespace NetCommerce.Kernel.Core.Domain;

/// <summary>
///     Abstract base for strongly-typed identifiers.
///     Provides type safety and eliminates primitive obsession for entity IDs.
/// </summary>
/// <typeparam name="TValue">The underlying value type (usually Guid, long, or string)</typeparam>
public abstract class TypedId<TValue> : IEquatable<TypedId<TValue>> where TValue : notnull
{
    protected TypedId(TValue value)
    {
        Value = value;
    }

    public TValue Value { get; }

    public bool Equals(TypedId<TValue>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return GetType() == other.GetType() && EqualityComparer<TValue>.Default.Equals(Value, other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is TypedId<TValue> typedId && Equals(typedId);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString() ?? string.Empty;
    }

    public static bool operator ==(TypedId<TValue>? left, TypedId<TValue>? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(TypedId<TValue>? left, TypedId<TValue>? right)
    {
        return !Equals(left, right);
    }
}

/// <summary>
///     Convenience base class for GUID-based strongly-typed identifiers.
/// </summary>
public abstract class GuidTypedId : TypedId<Guid>
{
    protected GuidTypedId(Guid value) : base(value)
    {
    }

    /// <summary>
    ///     Creates a new random ID.
    /// </summary>
    public static Guid NewGuid() => Guid.NewGuid();
}
