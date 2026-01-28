using FluentValidation;
using NetCommerce.Basket.Infrastructure;
using NetCommerce.Catalog.Application.Products.Validators;
using NetCommerce.Catalog.Infrastructure;
using NetCommerce.Finance.Infrastructure;
using NetCommerce.Inventory.Infrastructure;
using NetCommerce.Media.Infrastructure;
using NetCommerce.Ordering.Infrastructure;
using NetCommerce.Payments.Infrastructure;
using NetCommerce.Shipping.Infrastructure;

namespace NetCommerce.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Add API services for Minimal API (no controllers).
    /// </summary>
    public static IServiceCollection AddApiServicesMinimal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // CORS - Production-safe configuration
        // SECURITY: Never use AllowAnyOrigin in production!
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["https://localhost:5001", "https://localhost:3000"]; // Safe defaults

        services.AddCors(options =>
        {
            options.AddPolicy("AllowConfigured", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials(); // Required for SignalR/WebSockets
            });

            // Strict policy for sensitive endpoints
            options.AddPolicy("StrictSameOrigin", policy =>
            {
                policy.WithOrigins(allowedOrigins.Take(1).ToArray()) // Only primary origin
                    .WithMethods("GET", "POST")
                    .WithHeaders("Content-Type", "Authorization")
                    .AllowCredentials();
            });
        });

        // Rate Limiting - Protection against DoS attacks
        services.AddRateLimiter(options =>
        {
            // Global rate limit: 100 requests per minute per IP
            options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext =>
                {
                    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                        clientIp,
                        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1),
                            QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                            QueueLimit = 10
                        });
                });

            // Strict policy for authentication endpoints (5 attempts per minute)
            options.AddPolicy("AuthStrict", httpContext =>
            {
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    clientIp,
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    });
            });

            // Webhook policy (higher limit for Stripe callbacks)
            options.AddPolicy("Webhook", httpContext =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    "stripe-webhook",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1000,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                        QueueLimit = 100
                    }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Too many requests",
                    message = "Rate limit exceeded. Please try again later.",
                    retryAfter = context.Lease.TryGetMetadata(
                        System.Threading.RateLimiting.MetadataName.RetryAfter,
                        out var retryAfter) ? retryAfter.TotalSeconds : 60
                }, cancellationToken);
            };
        });

        // FluentValidation - Wolverine uses this via WolverineFx.FluentValidation middleware
        services.AddValidatorsFromAssemblyContaining<CreateProductCommandValidator>();

        return services;
    }

    public static IServiceCollection AddModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register all modules (Identity is now handled by Keycloak)
        services.AddCatalogModule(configuration);
        services.AddBasketModule(configuration);
        services.AddOrderingModule(configuration);
        services.AddInventoryModule(configuration);
        services.AddPaymentsModule(configuration);
        services.AddMediaModule(configuration);
        services.AddFinanceModule();
        services.AddShippingModule(configuration);

        return services;
    }
}
