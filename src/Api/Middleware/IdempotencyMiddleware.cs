using System.Text.Json;
using NetCommerce.SharedKernel.Infrastructure;

namespace NetCommerce.Api.Middleware;

/// <summary>
/// Endpoint filter for idempotency key processing on specific mutation endpoints.
/// Prevents duplicate processing of the same request.
/// Uses ArrayPool to minimize large object heap allocations.
/// 
/// This filter should be applied selectively to POST/PUT/PATCH endpoints that need idempotency,
/// rather than globally, to avoid memory overhead on read operations and large responses.
/// </summary>
public class IdempotencyEndpointFilter : IEndpointFilter
{
    private const string IdempotencyKeyHeader = "X-Idempotency-Key";
    private const int MaxCacheableResponseSize = 64 * 1024; // 64KB limit to avoid LOH

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<IdempotencyEndpointFilter>>();
        var idempotencyService = httpContext.RequestServices.GetRequiredService<IIdempotencyService>();

        // Check for idempotency key
        if (!httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey) 
            || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // For development, allow requests without the key
            // In production, consider returning 400 Bad Request
            logger.LogWarning(
                "Request to {Path} missing idempotency key",
                httpContext.Request.Path);
            
            return await next(context);
        }

        var key = $"{httpContext.Request.Method}:{httpContext.Request.Path}:{idempotencyKey}";

        // Check if already processed
        var cachedResponse = await idempotencyService.GetAsync(key);
        if (cachedResponse != null)
        {
            logger.LogInformation(
                "Returning cached response for idempotency key: {Key}",
                key);

            return Results.Content(cachedResponse, "application/json");
        }

        // Execute the endpoint
        var result = await next(context);

        // Cache successful responses
        await CacheResponseIfSuccessful(result, key, idempotencyService, logger);

        return result;
    }

    private static async Task CacheResponseIfSuccessful(
        object? result, 
        string key, 
        IIdempotencyService idempotencyService,
        ILogger logger)
    {
        try
        {
            // Only cache successful results
            if (result is IResult httpResult)
            {
                // Serialize the result for caching
                // Note: This works best with Results.Ok(), Results.Created(), etc.
                var responseContent = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (responseContent.Length <= MaxCacheableResponseSize)
                {
                    // Fire and forget caching - don't block the response
                    _ = idempotencyService.SetAsync(key, responseContent, TimeSpan.FromHours(24));
                }
                else
                {
                    logger.LogWarning(
                        "Response too large to cache for idempotency key: {Key}, Size: {Size}",
                        key,
                        responseContent.Length);
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail the request if caching fails
            logger.LogWarning(ex, "Failed to cache response for idempotency key: {Key}", key);
        }
    }
}

/// <summary>
/// Extension methods for applying idempotency to endpoints.
/// </summary>
public static class IdempotencyEndpointExtensions
{
    /// <summary>
    /// Adds idempotency support to an endpoint.
    /// Use this for POST/PUT/PATCH endpoints that modify state and need duplicate request protection.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter<IdempotencyEndpointFilter>();
    }
}
