#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetCommerce.Kernel.AspNetCore;

/// <summary>
///     Kestrel configuration extensions for enterprise-hardened web hosting.
/// </summary>
public static class KestrelExtensions
{
    /// <summary>
    ///     Configures Kestrel with enterprise-hardened security and performance settings.
    /// </summary>
    public static void AddEnterpriseWebHost(this WebApplicationBuilder builder)
    {
        // 1. Hardening Kestrel via Services Configuration
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            options.AddServerHeader = false; // Security: Hide version
            options.AllowResponseHeaderCompression = true;

            // Cleartext container ports (Docker/K8s terminate TLS at the
            // ingress/ALB) support HTTP/1.1 + HTTP/2 only. Enabling HTTP/3 on a
            // non-TLS endpoint crashes Kestrel at startup with
            // "HTTP/3 requires HTTPS" (ASPNETCORE_URLS=http://+:8080).
            options.ConfigureEndpointDefaults(o => o.Protocols = HttpProtocols.Http1AndHttp2);

            // Enterprise Limits: Prevent DoS
            options.Limits.MaxRequestBodySize = 52_428_800; // 50MB Default
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
        });

        // 2. Modern Output Caching (For Catalog/Search UI)
        builder.Services.AddOutputCache(options =>
        {
            options.AddBasePolicy(builder => builder
                .With(c => c.HttpContext.Request.Method == "GET") // Only cache GET
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByQuery("*"));
        });

        // 3. Hardened Form Options
        builder.Services.Configure<FormOptions>(o =>
        {
            o.ValueLengthLimit = 10 * 1024 * 1024; // 10MB limit for form fields
            o.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50MB limit for file uploads
            o.MemoryBufferThreshold = 1024 * 1024; // Buffer to disk after 1MB
        });

        builder.Services.AddRequestTimeouts(options =>
        {
            // A registered-without-policy AddRequestTimeouts is a no-op: every
            // request ran unbounded. The default bound converts a hung
            // downstream (Stripe, Keycloak, Meili, a wedged query) into a 503
            // instead of a held thread. Npgsql commands already bound tighter
            // (15s), HttpClients carry their own timeouts — this is the outer
            // backstop. Long-lived endpoints opt OUT explicitly at the map site
            // (the SignalR hub disables it; see MapEnterpriseEndpoints).
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(90),
                TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable
            };
        });

        // Note: Response Compression is already in your Program.cs
    }

    /// <summary>
    ///     Configures enterprise-hardened middleware pipeline.
    /// </summary>
    public static WebApplication UseEnterpriseWebHost(this WebApplication app)
    {
        app.UseRequestTimeouts();
        app.UseOutputCache();

        // Ensure HSTS is only used in Production
        if (app.Environment.EnvironmentName != Environments.Development)
        {
            app.UseHsts();
        }

        return app;
    }
}
