using NetCommerce.Api.Endpoints;
using NetCommerce.Api.Extensions;
using NetCommerce.Api.Middleware;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Kernel.Security.Authentication;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence;
using Wolverine.Http;
using Wolverine.SignalR;

namespace NetCommerce.Api.Extensions.Hosting;

/// <summary>
///     Request pipeline, endpoint maps, and migration runners. Extracted verbatim from
///     Program.cs. Middleware ORDER IS THE CONTRACT — ForwardedHeaders before security,
///     RateLimiter before CORS/Auth, Antiforgery after endpoint-aware middleware.
///     Do not reorder without a security review.
/// </summary>
public static class PipelineExtensions
{
    /// <summary>
    ///     CI/CD Migration Runner Mode (--migrate-only). Returns an exit code when the flag
    ///     is present, null otherwise. EF Core migrations require dynamic code (IL3050) and
    ///     therefore cannot run inside the Native AOT production container; pipelines execute
    ///     the JIT-built binary with this flag as a dedicated pre-deployment step.
    /// </summary>
    public static async Task<int?> RunMigrationsCliAsync(this WebApplication app, string[] args)
    {
        if (!args.Contains("--migrate-only"))
            return null;

        Console.WriteLine("[MIGRATION RUNNER] Running PostgreSQL schema migrations across all bounded contexts...");

#pragma warning disable IL3050 // EF Core migrations require dynamic code - this is a JIT pipeline step, never the AOT container
        await app.Services.ApplyMigrationsAsync<CatalogDbContext>();
        await app.Services.ApplyMigrationsAsync<OrderingDbContext>();
        await app.Services.ApplyMigrationsAsync<InventoryDbContext>();
        await app.Services.ApplyMigrationsAsync<PaymentsDbContext>();
        await app.Services.ApplyMigrationsAsync<FinanceDbContext>();
        await app.Services.ApplyMigrationsAsync<ShippingDbContext>();
#pragma warning restore IL3050

        Console.WriteLine("[MIGRATION RUNNER] Schema migrations complete. Exiting clean.");
        return 0;
    }

    /// <summary>
    ///     Resilient database migrations for Dev or explicit AutoMigrate flag. Skipped when
    ///     running a JasperFx CLI command (e.g. 'codegen write') — the app is not serving and
    ///     must not require a live database.
    /// </summary>
    public static async Task EnsureMigratedForDevAsync(this WebApplication app, string[] args)
    {
        if ((app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("AutoMigrate"))
            && !args.Contains("--migrate-only")
            && !args.Contains("codegen"))
        {
#pragma warning disable IL3050 // EF Core migrations require dynamic code - this is dev-only
            await app.Services.ApplyMigrationsAsync<CatalogDbContext>();
            await app.Services.ApplyMigrationsAsync<OrderingDbContext>();
            await app.Services.ApplyMigrationsAsync<InventoryDbContext>();
            await app.Services.ApplyMigrationsAsync<PaymentsDbContext>();
            await app.Services.ApplyMigrationsAsync<FinanceDbContext>();
            await app.Services.ApplyMigrationsAsync<ShippingDbContext>();
#pragma warning restore IL3050
        }
    }

    /// <summary>
    ///     Middleware pipeline in the exact order established in Program.cs.
    /// </summary>
    public static WebApplication UseEnterprisePipeline(this WebApplication app)
    {
        // Forwarded Headers (MUST be before RateLimiter, SecurityHeaders, Auth).
        // Resolves RemoteIpAddress behind ALB/Nginx/Cloudflare; prevents IP spoofing DoS.
        app.UseForwardedHeaders();

        // CRITICAL: Security Headers Middleware (MUST BE FIRST after ForwardedHeaders)
        app.UseNetCommerceSecurityHeaders();

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseResponseCompression();

        app.UseMiddleware<CorrelationIdMiddleware>();
        // Note: IdempotencyMiddleware removed from global pipeline.
        // Idempotency is now applied selectively to mutation endpoints via .WithIdempotency() filter.
        // This prevents memory overhead from response buffering on read operations.

        if (app.Environment.IsDevelopment()) app.UseNetCommerceOpenApi();

        // Enterprise-Hardened Web Host Middleware
        app.UseEnterpriseWebHost();

        app.UseHttpsRedirection();
        app.UseRateLimiter(); // Rate limiting before CORS/Auth - prevents DoS
        app.UseCors("AllowConfigured");
        app.UseAuthentication();
        app.UseAuthorization();

        // Zero-Trust Middleware (Token Introspection / Kill Switch).
        // When enabled (Auth__IntrospectionEnabled=true), this middleware validates
        // every token against Keycloak's introspection endpoint. If a user is banned
        // or their token is revoked, they are blocked immediately - not when JWT expires.
        app.UseZeroTrustMiddleware();

        // Defense in Depth: Antiforgery Middleware
        app.UseAntiforgery();

        return app;
    }

    /// <summary>
    ///     Endpoint maps: Aspire health checks, versioned Minimal APIs, SignalR hub,
    ///     and Wolverine.Http attribute endpoints. Map order is irrelevant (registration only).
    /// </summary>
    public static WebApplication MapEnterpriseEndpoints(this WebApplication app)
    {
        // Aspire Default Endpoints (Health checks)
        app.MapDefaultEndpoints();

        var versionSet = app.GetDefaultApiVersionSet();

        // Explicit endpoint registration for Native AOT compatibility.
        // Eliminates reflection-based assembly scanning (Assembly.GetTypes() / Activator.CreateInstance).
        app.MapNetCommerceEndpoints(versionSet);

        // SignalR Hub for Real-Time Order Notifications.
        // Wolverine's built-in WolverineHub provides WebSocket messaging to browsers.
        // Frontend connects to this endpoint to receive order status updates.
        app.MapWolverineSignalRHub("/api/messages");

        // Wolverine.Http Endpoints (Zero-Ceremony, Attribute-Based).
        // Maps endpoints decorated with [WolverineGet], [WolverinePost], etc.
        // These endpoints benefit from Wolverine's compound handler pattern,
        // automatic cascading messages, and transactional outbox integration.
        app.MapWolverineEndpoints();

        return app;
    }
}
