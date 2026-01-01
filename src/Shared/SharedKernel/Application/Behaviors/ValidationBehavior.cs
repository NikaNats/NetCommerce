using FluentValidation;
using MediatR;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.SharedKernel.Application.Behaviors;

/// <summary>
///     Pipeline behavior for validating commands and queries using FluentValidation.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
        {
            var errorMessage = string.Join("; ", failures.Select(f => f.ErrorMessage));

            // Create failure result using reflection for generic type
            return CreateValidationFailure(errorMessage);
        }

        return await next();
    }

    private static TResponse CreateValidationFailure(string errorMessage)
    {
        var resultType = typeof(TResponse);

        if (resultType == typeof(Result)) return (TResponse)Result.Failure(Error.Validation(errorMessage));

        // Handle Result<TValue>
        var genericType = resultType.GetGenericArguments()[0];
        var failureMethod = typeof(Result)
            .GetMethod(nameof(Result.Failure), [typeof(Error)])!
            .MakeGenericMethod(genericType);

        return (TResponse)failureMethod.Invoke(null, [Error.Validation(errorMessage)])!;
    }
}