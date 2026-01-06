#nullable enable
using NetCommerce.Kernel.SourceGenerators;

namespace NetCommerce.Domain.Orders;

/// <summary>
/// User-ის უნიკალური იდენტიფიკატორი.
/// Boilerplate (Parse, TryParse, ToString, EfValueConverter) გენერირდება ავტომატურად.
/// </summary>
[StronglyTypedId]
public partial record struct UserId(Guid Value) : NetCommerce.Kernel.Core.Ids.IStronglyTypedId;
