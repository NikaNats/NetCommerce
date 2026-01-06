#nullable enable
namespace NetCommerce.Kernel.Application;

/// <summary>
///     CQRS Query marker interface.
///     Queries represent read operations that don't change state.
/// </summary>
public interface IQuery<TResponse>;
