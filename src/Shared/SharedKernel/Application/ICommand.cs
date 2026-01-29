using NetCommerce.Kernel.Core.Results;

namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     CQRS Command marker interface.
///     Commands represent write operations that change state.
///     Wolverine discovers handlers by convention - no base interface required.
/// </summary>
public interface ICommand;

/// <summary>
///     CQRS Command with response value.
///     The TResponse type is used by Wolverine's cascading messages.
/// </summary>
public interface ICommand<TResponse> : ICommand;
