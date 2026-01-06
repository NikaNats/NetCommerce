#nullable enable
using System.Diagnostics;
using System.Net.Mime;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NetCommerce.Api.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IWebHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        // 1. Map Exception to status code and Machine-Readable Error Codes
        var (statusCode, errorCode) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "VALIDATION_FAILED"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "UNAUTHORIZED"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "BUSINESS_RULE_VIOLATION"),
            OperationCanceledException => (StatusCodes.Status408RequestTimeout, "REQUEST_TIMEOUT"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_SERVER_ERROR")
        };

        // 2. Categorized Logging (Best Practice #4)
        // Only 5xx errors trigger an 'Error' level log (and subsequent alerts)
        if (statusCode >= 500)
        {
            logger.LogError(exception,
                "[{ErrorCode}] Server-side error. TraceId: {TraceId}. Path: {Path}",
                errorCode, traceId, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "[{ErrorCode}] Client-side error. TraceId: {TraceId}. Message: {Message}",
                errorCode, traceId, exception.Message);
        }

        // 3. Construct RFC 9457 Problem Details
        ProblemDetails problemDetails;

        if (exception is ValidationException ve)
        {
            var errors = ve.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            problemDetails = new ValidationProblemDetails(errors)
            {
                Status = statusCode,
                Title = "Validation Failed",
                Detail = "One or more validation errors occurred.",
                Type = "https://netcommerce.com/errors/validation-failed"
            };
        }
        else
        {
            problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = GetTitle(exception),
                // 4. Absolute Sanitization (Best Practice #5)
                Detail = (statusCode == 500 && !env.IsDevelopment())
                         ? "An unexpected internal error occurred."
                         : exception.Message,
                Type = $"https://netcommerce.com/errors/{errorCode.ToLower().Replace("_", "-")}"
            };
        }

        // Set Instance for better traceability
        problemDetails.Instance = httpContext.Request.Path;

        // 5. Extensions for DX (Machine-readable code & Tracing)
        problemDetails.Extensions["code"] = errorCode; // Best Practice #6
        problemDetails.Extensions["traceId"] = traceId; // Best Practice #3

        // 6. Smart Retry Hints (Best Practice #9)
        if (statusCode == StatusCodes.Status503ServiceUnavailable || statusCode == StatusCodes.Status408RequestTimeout)
        {
            problemDetails.Extensions["retryable"] = true;
            if (statusCode == StatusCodes.Status503ServiceUnavailable)
            {
                httpContext.Response.Headers.RetryAfter = "30";
            }
        }

        if (env.IsDevelopment())
        {
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        // 7. Final Response (RFC-compliant Content-Type)
        httpContext.Response.StatusCode = statusCode;
        // Note: WriteAsJsonAsync automatically sets the content-type to application/problem+json
        // if the object is ProblemDetails, but being explicit doesn't hurt.
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static string GetTitle(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Unauthorized Access",
        KeyNotFoundException => "Resource Not Found",
        InvalidOperationException => "Business Rule Violation",
        _ => "Internal Server Error"
    };
}
