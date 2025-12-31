using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NetCommerce.Api.Middleware;

/// <summary>
/// Global exception handler that returns consistent problem details responses.
/// </summary>
public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception occurred. TraceId: {TraceId}, Machine: {Machine}",
            traceId,
            Environment.MachineName);

        // Handle FluentValidation exceptions
        if (exception is FluentValidation.ValidationException validationException)
        {
            var validationProblem = new ValidationProblemDetails(
                validationException.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Title = "Validation Failed",
                Status = StatusCodes.Status400BadRequest,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Instance = httpContext.Request.Path
            };
            validationProblem.Extensions["traceId"] = traceId;

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(validationProblem, cancellationToken);
            return true;
        }

        // Handle other exception types
        var (statusCode, title, type) = exception switch
        {
            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "https://tools.ietf.org/html/rfc7235#section-3.1"),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Resource Not Found",
                "https://tools.ietf.org/html/rfc7231#section-6.5.4"),
            InvalidOperationException => (
                StatusCodes.Status400BadRequest,
                "Invalid Operation",
                "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An error occurred while processing your request",
                "https://tools.ietf.org/html/rfc7231#section-6.6.1")
        };

        var problemDetails = new ProblemDetails
        {
            Title = title,
            Status = statusCode,
            Type = type,
            Instance = httpContext.Request.Path,
            Detail = exception is InvalidOperationException ? exception.Message : null
        };
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
