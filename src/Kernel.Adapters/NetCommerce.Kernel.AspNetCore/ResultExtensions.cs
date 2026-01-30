#nullable enable
using Microsoft.AspNetCore.Http;
using NetCommerce.Kernel.Core.Results;
using System.Diagnostics;

namespace NetCommerce.Kernel.AspNetCore;

/// <summary>
/// ASP.NET Core HTTP adapter for Kernel Result types.
/// Provides RFC 9457 Problem Details compliant error responses with distributed tracing support.
/// This is the SINGLE SOURCE OF TRUTH for Result-to-HTTP mappings in NetCommerce.
/// </summary>
/// <remarks>
/// Design Principles:
/// - Kernel stays technology-agnostic (no HTTP concerns)
/// - This adapter bridges domain Results to ASP.NET Core HTTP responses
/// - All HTTP status code mapping logic is centralized here
/// - Supports both legacy Error and modern Rfc9457Error formats
/// - Automatically injects OpenTelemetry TraceId for observability
/// - Problem type URIs are configurable (localhost in dev, docs site in prod)
/// </remarks>
public static class ResultExtensions
{
    /// <summary>
    /// Default base URI for problem type URIs when no configuration is provided.
    /// Can be overridden via ProblemDetailsOptions configuration.
    /// </summary>
    private const string DefaultProblemBaseUri = "https://netcommerce.io/problems";

    /// <summary>
    /// Converts Result&lt;T&gt; to IResult.
    /// Returns 200 OK with value on success, RFC 9457 Problem Details on failure.
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result, HttpContext? httpContext = null)
    {
        if (result.IsSuccess)
        {
            return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);
        }

        return result.Error.ToHttpResult(httpContext);
    }

    /// <summary>
    /// Converts Result to IResult.
    /// Returns 204 No Content on success, RFC 9457 Problem Details on failure.
    /// </summary>
    public static IResult ToHttpResult(this Result result, HttpContext? httpContext = null)
    {
        return result.IsSuccess ? Results.NoContent() : result.Error.ToHttpResult(httpContext);
    }

    /// <summary>
    /// Converts Result&lt;T&gt; to 201 Created with Location header.
    /// </summary>
    public static IResult ToCreatedResult<T>(this Result<T> result, string? uri = null, HttpContext? httpContext = null)
    {
        if (result.IsSuccess)
        {
            return Results.Created(uri ?? string.Empty, result.Value);
        }

        return result.Error.ToHttpResult(httpContext);
    }

    /// <summary>
    /// Converts Result&lt;T&gt; to 202 Accepted for async operations.
    /// Includes status polling URI for long-running workflows.
    /// </summary>
    public static IResult ToAcceptedResult<T>(this Result<T> result, string statusUri, HttpContext? httpContext = null)
    {
        if (result.IsSuccess)
        {
            return Results.Accepted(statusUri, new
            {
                status = "InProgress",
                statusUri,
                result.Value
            });
        }

        return result.Error.ToHttpResult(httpContext);
    }

    /// <summary>
    /// Converts Error to RFC 9457 Problem Details IResult.
    /// Handles both legacy Error format (converts to RFC 9457).
    /// Automatically injects TraceId for distributed tracing.
    /// URI generation uses configured base URI (localhost in dev, docs in prod).
    /// </summary>
    public static IResult ToHttpResult(this Error error, HttpContext? httpContext = null)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();

        // Legacy Error: Map to RFC 9457 format
        var statusCode = error.StatusCode != 0 ? error.StatusCode : MapErrorCodeToStatusCode(error.Code);
        var baseUri = GetProblemBaseUri(httpContext);
        var problemType = $"{baseUri}/{error.Code.ToLowerInvariant().Replace('.', '-')}";
        var title = error.Code.Split('.').LastOrDefault() ?? "Error";

        return Results.Problem(
            type: problemType,
            title: title,
            detail: error.Description,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["traceId"] = traceId }
        );
    }

    /// <summary>
    /// Gets the configured problem details base URI from options or returns default.
    /// Allows dev/prod environment-specific URIs (e.g., http://localhost:5000/errors vs https://docs.netcommerce.io/errors).
    /// </summary>
    private static string GetProblemBaseUri(HttpContext? httpContext)
    {
        if (httpContext is null) return DefaultProblemBaseUri;

        try
        {
            var generator = httpContext.RequestServices.GetService(typeof(ProblemDetailsUriGenerator)) as ProblemDetailsUriGenerator;
            return generator?.BaseUri ?? DefaultProblemBaseUri;
        }
        catch
        {
            return DefaultProblemBaseUri;
        }
    }

    /// <summary>
    /// Converts Rfc9457Error directly to IResult.
    /// Used when errors are already in RFC 9457 format.
    /// </summary>
    public static IResult ToHttpResult(this Rfc9457Error error)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        var extensions = error.Extensions is not null
            ? new Dictionary<string, object?>(error.Extensions.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value)))
            : new Dictionary<string, object?>();

        if (!extensions.ContainsKey("traceId"))
        {
            extensions["traceId"] = traceId;
        }

        return Results.Problem(
            type: error.Type,
            title: error.Title,
            detail: error.Detail,
            statusCode: error.Status,
            instance: error.Instance,
            extensions: extensions
        );
    }

    /// <summary>
    /// Maps legacy error codes to HTTP status codes.
    /// Centralized status code mapping following RESTful best practices.
    /// </summary>
    /// <remarks>
    /// Status Code Conventions:
    /// - 400 Bad Request: Client input validation failures
    /// - 401 Unauthorized: Authentication required
    /// - 403 Forbidden: Insufficient permissions
    /// - 404 Not Found: Resource doesn't exist
    /// - 409 Conflict: Optimistic concurrency or business rule violations
    /// - 422 Unprocessable Entity: Semantic validation failures
    /// - 500 Internal Server Error: Unexpected system failures
    /// </remarks>
    private static int MapErrorCodeToStatusCode(string errorCode)
    {
        return errorCode.ToLowerInvariant() switch
        {
            var c when c.Contains("notfound") => StatusCodes.Status404NotFound,
            var c when c.Contains("validation") => StatusCodes.Status422UnprocessableEntity,
            var c when c.Contains("conflict") => StatusCodes.Status409Conflict,
            var c when c.Contains("unauthorized") => StatusCodes.Status401Unauthorized,
            var c when c.Contains("forbidden") => StatusCodes.Status403Forbidden,
            var c when c.Contains("nullvalue") => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    // ===== BACKWARD COMPATIBILITY ALIASES =====
    // These methods provide API-friendly names for cleaner endpoint code.
    // All logic delegates to the canonical ToHttpResult methods above.

    /// <summary>
    /// Alias for ToHttpResult - provides API-friendly naming.
    /// </summary>
    public static IResult ToApiResult<T>(this Result<T> result) => result.ToHttpResult();

    /// <summary>
    /// Alias for ToHttpResult - provides API-friendly naming.
    /// </summary>
    public static IResult ToApiResult(this Result result) => result.ToHttpResult();
}
