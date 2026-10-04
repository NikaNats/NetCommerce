using FluentValidation;
using NetCommerce.Api.Serialization;
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

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    ///     Add API services for Minimal API (no controllers).
    /// </summary>
    public static IServiceCollection AddApiServicesMinimal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // CORS - explicit origins, fail-closed outside Development.
        // SECURITY: Never use AllowAnyOrigin in production! An unset/empty
        // allowlist previously fell back to localhost origins, which in
        // production either blocks the real storefront (visible breakage) or
        // tempts an AllowAnyOrigin "fix" (fail-open). So: Development keeps the
        // localhost convenience defaults; every other environment throws at
        // startup naming the missing Cors:AllowedOrigins value.
        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? [];
        var allowedOrigins = configuredOrigins
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .ToArray();

        if (allowedOrigins.Length == 0)
        {
            var environmentName = configuration["ASPNETCORE_ENVIRONMENT"]
                ?? configuration["DOTNET_ENVIRONMENT"]
                ?? Microsoft.Extensions.Hosting.Environments.Production;
            // Development AND Testing keep localhost convenience defaults: test
            // hosts (WebApplicationFactory/TestServer) never touch the network,
            // so origins are meaningless there and strictness would only break
            // the suite. Every other environment fails fast below.
            if (string.Equals(environmentName, Microsoft.Extensions.Hosting.Environments.Development, StringComparison.OrdinalIgnoreCase)
                || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            {
                allowedOrigins = ["https://localhost:5001", "https://localhost:3000"];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cors:AllowedOrigins must list the storefront origin(s) in {environmentName}. " +
                    "Unset means unknown, and unknown must not become localhost-with-credentials.");
            }
        }

        services.AddCors(options =>
        {
            options.AddPolicy("AllowConfigured", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials(); // Required for SignalR/WebSockets
            });

            // Strict policy for sensitive endpoints. The FIRST configured origin
            // is the primary storefront — keep it first in Cors:AllowedOrigins.
            options.AddPolicy("StrictSameOrigin", policy =>
            {
                policy.WithOrigins(allowedOrigins.Take(1).ToArray()) // Only primary origin
                    .WithMethods("GET", "POST")
                    .WithHeaders("Content-Type", "Authorization")
                    .AllowCredentials();
            });
        });

        // Rate Limiting - Protection against DoS attacks
        // NOTE: HttpContext.Connection.RemoteIpAddress is populated by ForwardedHeadersMiddleware (configured in Program.cs).
        // Do NOT trust X-Forwarded-For directly; KnownNetworks/KnownProxies must be allow-listed.
        services.AddRateLimiter(options =>
        {
            // Global rate limit: 100 requests per minute per partition (user-or-IP aware)
            options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext =>
                {
                    var partitionKey = httpContext.GetRateLimitPartitionKey();
                    return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey,
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
                var partitionKey = httpContext.GetRateLimitPartitionKey();
                return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
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

            // Per-user token bucket policy: Authenticated users get higher limits but are
            // individually tracked. Falls back to IP partitioning for anonymous requests.
            // Token bucket allows bursts while maintaining average rate.
            options.AddPolicy("PerUser", httpContext =>
            {
                var partitionKey = httpContext.GetRateLimitPartitionKey();

                return System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey,
                    _ => new System.Threading.RateLimiting.TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 60,                                          // Max burst capacity
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),           // Refill interval
                        TokensPerPeriod = 10,                                     // Tokens added per interval
                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5,
                        AutoReplenishment = true
                    });
            });

            // Admin-specific strict rate limit: Lower limits for destructive admin operations
            options.AddPolicy("AdminStrict", httpContext =>
            {
                var userId = httpContext.User.FindFirst("sub")?.Value ?? "unknown-admin";
                return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    $"admin:{userId}",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    });
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var retrySeconds = context.Lease.TryGetMetadata(
                    System.Threading.RateLimiting.MetadataName.RetryAfter,
                    out var retryAfter) ? retryAfter.TotalSeconds : 60;
                var response = new RateLimitResponse
                {
                    Error = "Too many requests",
                    Message = "Rate limit exceeded. Please try again later.",
                    RetryAfter = retrySeconds
                };
                await context.HttpContext.Response.WriteAsJsonAsync(
                    response, ApiJsonContext.Default.RateLimitResponse, cancellationToken: cancellationToken);
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
        services.AddFinanceModule(configuration);
        services.AddShippingModule(configuration);

        // Basket -> Catalog pricing seam.
        //
        // The composition root is the only place that knows BOTH modules, so this is
        // where IProductPriceSource gets its implementation. Basket itself never
        // references Catalog; it only declares the port. Registering it here rather
        // than inside AddBasketModule keeps that dependency pointing inward.
        services.AddScoped<NetCommerce.Basket.Application.IProductPriceSource,
            NetCommerce.Api.Endpoints.Basket.CatalogPriceSource>();

        return services;
    }
}
