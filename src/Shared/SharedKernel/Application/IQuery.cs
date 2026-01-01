using MediatR;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     CQRS Query marker interface.
///     Queries represent read operations that don't change state.
/// </summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>
///     Query handler interface.
/// </summary>
public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;