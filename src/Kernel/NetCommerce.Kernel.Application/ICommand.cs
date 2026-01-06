#nullable enable
namespace NetCommerce.Kernel.Application;

/// <summary>
///     CQRS Command marker interface.
///     Commands represent write operations that change state.
/// </summary>
public interface ICommand;

/// <summary>
///     CQRS Command with response value.
/// </summary>
public interface ICommand<TResponse> : ICommand;
