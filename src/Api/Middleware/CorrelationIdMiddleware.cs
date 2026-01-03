#nullable enable
using System.Diagnostics;

namespace NetCommerce.Api.Middleware;

/// <summary>
///     Middleware for tracking requests with a correlation ID.
///     Enables distributed tracing across services.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        // Add to response headers
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.TryAdd(CorrelationIdHeader, correlationId);
            return Task.CompletedTask;
        });

        // Add to activity for distributed tracing
        Activity.Current?.SetTag("correlation.id", correlationId);

        // Add to logging scope
        using (context.RequestServices.GetRequiredService<ILogger<CorrelationIdMiddleware>>()
                   .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId)
            && !string.IsNullOrWhiteSpace(correlationId))
        {
            var id = correlationId.ToString();
            context.Items["CorrelationId"] = id;
            return id;
        }

        var newId = Guid.NewGuid().ToString("D");
        context.Items["CorrelationId"] = newId;
        return newId;
    }
}

/// <summary>
///     Extension methods for correlation ID middleware.
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>
    ///     Adds correlation ID middleware to the pipeline.
    ///     This should be added early in the pipeline for maximum tracing coverage.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    /// <summary>
    ///     Gets the current correlation ID from the HttpContext.
    /// </summary>
    public static string? GetCorrelationId(this HttpContext context)
    {
        return context.Items.TryGetValue("CorrelationId", out var correlationId)
            ? correlationId?.ToString()
            : null;
    }
}
