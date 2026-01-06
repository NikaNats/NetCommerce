#nullable enable
namespace NetCommerce.Kernel.Core.Results;

/// <summary>
///     Represents the result of an operation that can succeed or fail.
///     Used for explicit error handling without exceptions.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Success result cannot have an error");

        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failure result must have an error");

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success()
    {
        return new Result(true, Error.None);
    }

    public static Result Failure(Error error)
    {
        return new Result(false, error);
    }

    public static Result<TValue> Success<TValue>(TValue value)
    {
        return new Result<TValue>(value, true, Error.None);
    }

    public static Result<TValue> Failure<TValue>(Error error)
    {
        return new Result<TValue>(default, false, error);
    }

    public static Result<TValue> Create<TValue>(TValue? value)
    {
        return value is not null ? Success(value) : Failure<TValue>(Error.NullValue);
    }
}

/// <summary>
///     Represents the result of an operation that returns a value.
/// </summary>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access value of a failed result");

    public static implicit operator Result<TValue>(TValue? value)
    {
        return Create(value);
    }
}

/// <summary>
///     Represents an error with code and description.
/// </summary>
public sealed record Error(string Code, string Description)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null");

    public static Error NotFound(string entityName, object id)
    {
        return new Error($"{entityName}.NotFound", $"{entityName} with id '{id}' was not found");
    }

    public static Error Validation(string description)
    {
        return new Error("Validation.Error", description);
    }

    public static Error Conflict(string description)
    {
        return new Error("Conflict.Error", description);
    }

    public static Error Unauthorized(string description = "Unauthorized access")
    {
        return new Error("Unauthorized.Error", description);
    }

    public static Error Forbidden(string description = "Access forbidden")
    {
        return new Error("Forbidden.Error", description);
    }

    public static Error Failure(string code, string description)
    {
        return new Error(code, description);
    }
}
