using System.Diagnostics.CodeAnalysis;

namespace NetCommerce.SharedKernel.Results;

/// <summary>
/// Represents the result of an operation that can succeed or fail.
/// Used for explicit error handling without exceptions.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Success result cannot have an error");
        
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failure result must have an error");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    public static Result<TValue> Create<TValue>(TValue? value) =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue);
}

/// <summary>
/// Represents the result of an operation that returns a value.
/// </summary>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    public TValue Value => IsSuccess 
        ? _value! 
        : throw new InvalidOperationException("Cannot access value of a failed result");

    internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public static implicit operator Result<TValue>(TValue? value) => Create(value);
}

/// <summary>
/// Represents an error with code and description.
/// </summary>
public sealed record Error(string Code, string Description)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null");
    
    public static Error NotFound(string entityName, object id) => 
        new($"{entityName}.NotFound", $"{entityName} with id '{id}' was not found");
    
    public static Error Validation(string description) => 
        new("Validation.Error", description);
    
    public static Error Conflict(string description) => 
        new("Conflict.Error", description);
    
    public static Error Unauthorized(string description = "Unauthorized access") => 
        new("Unauthorized.Error", description);
    
    public static Error Forbidden(string description = "Access forbidden") => 
        new("Forbidden.Error", description);

    public static Error Failure(string code, string description) =>
        new(code, description);
}

/// <summary>
/// Extension methods for Result type.
/// </summary>
public static class ResultExtensions
{
    public static Result<TOut> Map<TIn, TOut>(
        this Result<TIn> result, 
        Func<TIn, TOut> mapper)
    {
        return result.IsSuccess 
            ? Result.Success(mapper(result.Value)) 
            : Result.Failure<TOut>(result.Error);
    }

    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask, 
        Func<TIn, TOut> mapper)
    {
        var result = await resultTask;
        return result.Map(mapper);
    }

    public static Result<TOut> Bind<TIn, TOut>(
        this Result<TIn> result, 
        Func<TIn, Result<TOut>> func)
    {
        return result.IsSuccess ? func(result.Value) : Result.Failure<TOut>(result.Error);
    }

    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask, 
        Func<TIn, Task<Result<TOut>>> func)
    {
        var result = await resultTask;
        return result.IsSuccess ? await func(result.Value) : Result.Failure<TOut>(result.Error);
    }

    public static TOut Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);
    }
}
