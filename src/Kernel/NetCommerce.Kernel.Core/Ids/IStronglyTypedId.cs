#nullable enable
using System;

namespace NetCommerce.Kernel.Core.Ids;

/// <summary>
///     Defines a Strongly Typed ID contract using Static Abstract Members (C# 11+).
///     Enables generic usage without Reflection.
/// </summary>
/// <typeparam name="TId">The concrete type of the ID.</typeparam>
public interface IStronglyTypedId<TId> :
    IEquatable<TId>,
    IComparable<TId>,
    IParsable<TId> // .NET 7+ standard for generic parsing
    where TId : struct, IStronglyTypedId<TId>
{
    /// <summary>
    ///     The underlying primitive value.
    /// </summary>
    Guid Value { get; }

    /// <summary>
    ///     Factory method to create a new instance (Zero-Allocation / AOT safe).
    ///     No Reflection required to call this.
    /// </summary>
    static abstract TId Create(Guid value);

    /// <summary>
    ///     Creates a new random ID.
    /// </summary>
    static TId New() => TId.Create(Guid.NewGuid());

    /// <summary>
    ///     Returns the empty/default value for this ID.
    /// </summary>
    static TId Empty => TId.Create(Guid.Empty);
}
