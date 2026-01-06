#nullable enable
using System.Diagnostics.CodeAnalysis;
using NetCommerce.Kernel.SourceGenerators;

namespace NetCommerce.Kernel.Core.Ids;

/// <summary>
///     Marker interface for strongly typed IDs.
/// </summary>
public interface IStronglyTypedId
{
    /// <summary>
    ///     The underlying Guid value.
    /// </summary>
    Guid Value { get; }
}

/// <summary>
///     Example strongly typed ID for orders.
///     All boilerplate (Parse, TryParse, ToString, EfValueConverter) is generated automatically.
/// </summary>
[StronglyTypedId]
public partial record struct OrderId(Guid Value) : IStronglyTypedId;
