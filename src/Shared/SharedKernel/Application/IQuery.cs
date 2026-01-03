namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     CQRS Query marker interface.
///     Queries represent read operations that don't change state.
///     Wolverine discovers handlers by convention - no base interface required.
/// </summary>
public interface IQuery<TResponse>;
