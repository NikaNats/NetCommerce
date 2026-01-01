namespace NetCommerce.Api.Middleware;

/// <summary>
///     Middleware that enforces request body size limits to prevent DoS attacks.
///     Per ASP.NET Core best practices: "Don't assume that HttpRequest.ContentLength is not null"
/// </summary>
public class RequestSizeLimitMiddleware
{
    private const long DefaultMaxRequestBodySize = 10 * 1024 * 1024; // 10 MB default
    private readonly ILogger<RequestSizeLimitMiddleware> _logger;
    private readonly long _maxRequestBodySize;
    private readonly RequestDelegate _next;

    public RequestSizeLimitMiddleware(
        RequestDelegate next,
        ILogger<RequestSizeLimitMiddleware> logger,
        long maxRequestBodySize = DefaultMaxRequestBodySize)
    {
        _next = next;
        _logger = logger;
        _maxRequestBodySize = maxRequestBodySize;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check Content-Length header if present
        // Note: ContentLength being null means the length is unknown, not zero
        if (context.Request.ContentLength.HasValue &&
            context.Request.ContentLength.Value > _maxRequestBodySize)
        {
            _logger.LogWarning(
                "Request body size {Size} exceeds maximum allowed size {MaxSize} for {Path}",
                context.Request.ContentLength.Value,
                _maxRequestBodySize,
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.11",
                title = "Payload Too Large",
                status = 413,
                detail =
                    $"Request body size exceeds the maximum allowed size of {_maxRequestBodySize / (1024 * 1024)} MB."
            });
            return;
        }

        await _next(context);
    }
}

/// <summary>
///     Extension methods for RequestSizeLimitMiddleware.
/// </summary>
public static class RequestSizeLimitMiddlewareExtensions
{
    /// <summary>
    ///     Adds request size limit middleware to prevent large request body DoS attacks.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="maxRequestBodySizeInMB">Maximum request body size in megabytes. Default is 10 MB.</param>
    public static IApplicationBuilder UseRequestSizeLimit(
        this IApplicationBuilder app,
        int maxRequestBodySizeInMB = 10)
    {
        return app.UseMiddleware<RequestSizeLimitMiddleware>(
            (long)maxRequestBodySizeInMB * 1024 * 1024);
    }
}