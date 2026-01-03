#nullable enable

using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Api.Endpoints;

/// <summary>
///     Extension methods for handling Result patterns in Minimal API endpoints.
///     Returns RFC 7807 compliant Problem Details for errors.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    ///     Converts a Result&lt;T&gt; to an API result with proper HTTP status codes.
    ///     Returns 200 OK for success with value, 204 No Content for null values.
    /// </summary>
    public static IResult ToApiResult<T>(this Result<T> result)
    {
        if (result.IsSuccess) return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);

        return result.Error.ToProblemDetails();
    }

    /// <summary>
    ///     Converts a Result to an API result with proper HTTP status codes.
    ///     Returns 204 No Content for success.
    /// </summary>
    public static IResult ToApiResult(this Result result)
    {
        if (result.IsSuccess) return Results.NoContent();

        return result.Error.ToProblemDetails();
    }

    /// <summary>
    ///     Converts a successful Result to a 201 Created response with Location header.
    /// </summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, string? uri = null)
    {
        if (result.IsSuccess) return Results.Created(uri ?? string.Empty, result.Value);

        return result.Error.ToProblemDetails();
    }

    /// <summary>
    ///     Converts a successful Result to a 202 Accepted response for async operations.
    ///     Includes Location header for status polling endpoint.
    /// </summary>
    public static IResult ToAcceptedResult<T>(this Result<T> result, string statusUri)
    {
        if (result.IsSuccess)
            return Results.Accepted(statusUri, new
            {
                status = "In Progress",
                statusUri,
                result.Value
            });

        return result.Error.ToProblemDetails();
    }

    /// <summary>
    ///     Converts an Error to an RFC 7807 Problem Details response.
    /// </summary>
    private static IResult ToProblemDetails(this Error error)
    {
        return error.Code switch
        {
            _ when error.Code.Contains("NotFound", StringComparison.OrdinalIgnoreCase) =>
                Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Resource Not Found",
                    detail: error.Description,
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4"),

            _ when error.Code.Contains("Validation", StringComparison.OrdinalIgnoreCase) =>
                Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: error.Description,
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1"),

            _ when error.Code.Contains("Conflict", StringComparison.OrdinalIgnoreCase) =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: error.Description,
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.8"),

            _ when error.Code.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) =>
                Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: error.Description,
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1"),

            _ when error.Code.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) =>
                Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: error.Description,
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3"),

            _ when error.Code.Contains("Unprocessable", StringComparison.OrdinalIgnoreCase) =>
                Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Unprocessable Entity",
                    detail: error.Description,
                    type: "https://tools.ietf.org/html/rfc4918#section-11.2"),

            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: error.Description,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1")
        };
    }
}