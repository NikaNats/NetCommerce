using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace NetCommerce.Api.Endpoints.Common;

/// <summary>
///     Factory for creating RFC 7807 compliant Problem Details responses.
///     Provides standardized error responses following RESTful best practices.
/// </summary>
public static class ProblemDetailsFactory
{
    /// <summary>
    ///     Creates a 400 Bad Request problem details response.
    /// </summary>
    public static ProblemDetails BadRequest(string detail, string? instance = null,
        IDictionary<string, object?>? extensions = null)
    {
        return CreateProblemDetails(
            HttpStatusCode.BadRequest,
            "Bad Request",
            detail,
            "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            instance,
            extensions);
    }

    /// <summary>
    ///     Creates a 400 Bad Request with validation errors.
    /// </summary>
    public static ValidationProblemDetails ValidationError(IDictionary<string, string[]> errors,
        string? instance = null)
    {
        return new ValidationProblemDetails(errors)
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "One or more validation errors occurred.",
            Status = (int)HttpStatusCode.BadRequest,
            Instance = instance
        };
    }

    /// <summary>
    ///     Creates a 401 Unauthorized problem details response.
    /// </summary>
    public static ProblemDetails Unauthorized(string detail = "Authentication is required to access this resource.",
        string? instance = null)
    {
        return CreateProblemDetails(
            HttpStatusCode.Unauthorized,
            "Unauthorized",
            detail,
            "https://tools.ietf.org/html/rfc7235#section-3.1",
            instance);
    }

    /// <summary>
    ///     Creates a 403 Forbidden problem details response.
    /// </summary>
    public static ProblemDetails Forbidden(string detail = "You don't have permission to access this resource.",
        string? instance = null)
    {
        return CreateProblemDetails(
            HttpStatusCode.Forbidden,
            "Forbidden",
            detail,
            "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            instance);
    }

    /// <summary>
    ///     Creates a 404 Not Found problem details response.
    /// </summary>
    public static ProblemDetails NotFound(string resourceType, string identifier, string? instance = null)
    {
        return CreateProblemDetails(
            HttpStatusCode.NotFound,
            "Resource Not Found",
            $"The {resourceType} with identifier '{identifier}' was not found.",
            "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            instance,
            new Dictionary<string, object?>
            {
                ["resourceType"] = resourceType,
                ["identifier"] = identifier
            });
    }

    /// <summary>
    ///     Creates a 409 Conflict problem details response.
    /// </summary>
    public static ProblemDetails Conflict(string detail, string? instance = null,
        IDictionary<string, object?>? extensions = null)
    {
        return CreateProblemDetails(
            HttpStatusCode.Conflict,
            "Conflict",
            detail,
            "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            instance,
            extensions);
    }

    /// <summary>
    ///     Creates a 422 Unprocessable Entity problem details response.
    /// </summary>
    public static ProblemDetails UnprocessableEntity(string detail, string? instance = null,
        IDictionary<string, object?>? extensions = null)
    {
        return CreateProblemDetails(
            HttpStatusCode.UnprocessableEntity,
            "Unprocessable Entity",
            detail,
            "https://tools.ietf.org/html/rfc4918#section-11.2",
            instance,
            extensions);
    }

    /// <summary>
    ///     Creates a 500 Internal Server Error problem details response.
    /// </summary>
    public static ProblemDetails InternalServerError(string? traceId = null, string? instance = null)
    {
        var extensions = traceId is not null
            ? new Dictionary<string, object?> { ["traceId"] = traceId }
            : null;

        return CreateProblemDetails(
            HttpStatusCode.InternalServerError,
            "Internal Server Error",
            "An unexpected error occurred while processing your request.",
            "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            instance,
            extensions);
    }

    private static ProblemDetails CreateProblemDetails(
        HttpStatusCode statusCode,
        string title,
        string detail,
        string type,
        string? instance = null,
        IDictionary<string, object?>? extensions = null)
    {
        var problemDetails = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = (int)statusCode,
            Detail = detail,
            Instance = instance
        };

        if (extensions is not null)
            foreach (var (key, value) in extensions)
                problemDetails.Extensions[key] = value;

        return problemDetails;
    }
}