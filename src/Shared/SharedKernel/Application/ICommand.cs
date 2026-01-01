using MediatR;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     CQRS Command marker interface.
///     Commands represent write operations that change state.
/// </summary>
public interface ICommand : IRequest<Result>;

/// <summary>
///     CQRS Command with response value.
/// </summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>
///     Command handler interface.
/// </summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

/// <summary>
///     Command handler with response interface.
/// </summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;