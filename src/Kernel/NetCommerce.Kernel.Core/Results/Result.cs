#nullable enable
namespace NetCommerce.Kernel.Core.Results;

/// <summary>
///     Represents the result of an operation that can succeed or fail.
///     Used for explicit error handling without exceptions.
/// </summary>
public class Result(bool isSuccess, Error error)
{
    public bool IsSuccess { get; } = isSuccess;
    public bool IsFailure => !IsSuccess;
    public Error Error { get; } = error;

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

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
public class Result<TValue>(TValue? value, bool isSuccess, Error error)
    : Result(isSuccess, error)
{
    private readonly TValue? _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value of a failure result. Error: {Error.Description}");

    public static implicit operator Result<TValue>(TValue value) => new(value, true, Error.None);
}

/// <summary>
///     Represents an error with code and description.
/// </summary>
public sealed record Error(string Code, string Description, int StatusCode = 500)
{
    public static readonly Error None = new(string.Empty, string.Empty, 200);
    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null", 422);

    public static Error NotFound(string entityName, object id)
    {
        return new Error($"{entityName}.NotFound", $"{entityName} with id '{id}' was not found", 404);
    }

    public static Error Validation(string description)
    {
        return new Error("Validation.Error", description, 422);
    }

    public static Error Conflict(string description)
    {
        return new Error("Conflict.Error", description, 409);
    }

    public static Error Unauthorized(string description = "Unauthorized access")
    {
        return new Error("Unauthorized.Error", description, 401);
    }

    public static Error Forbidden(string description = "Access forbidden")
    {
        return new Error("Forbidden.Error", description, 403);
    }

    public static Error Failure(string code, string description)
    {
        return new Error(code, description);
    }
}

/// <summary>
/// RFC 9457 Compliant Error Model.
/// </summary>
public sealed record Rfc9457Error(
    string Type,          // URI reference (default: "about:blank")
    string Title,         // Short, human-readable summary
    int Status,           // HTTP status code
    string Detail,        // Specific explanation for this occurrence
    string? Instance = null, // URI for specific occurrence
    Dictionary<string, object>? Extensions = null) // Extension members (e.g., validation errors)
{
    public static readonly Rfc9457Error None = new("about:blank", "Success", 200, "Operation successful");

    // Predefined Factory methods for common types
    public static Rfc9457Error NotFound(string detail, string? instance = null) =>
        new("https://netcommerce.io/probs/not-found", "Resource Not Found", 404, detail, instance);

    public static Rfc9457Error Validation(string detail, Dictionary<string, object> validationErrors) =>
        new("https://netcommerce.io/probs/validation-error", "Your request is not valid.", 422, detail, null, validationErrors);

    public static Rfc9457Error Conflict(string detail) =>
        new("https://netcommerce.io/probs/conflict", "A conflict occurred.", 409, detail);
}

/// <summary>
///     RFC 9457 Problem Details representation.
/// </summary>
public record ProblemResult(string Type, string Title, int Status, string Detail, string? Instance = null);
