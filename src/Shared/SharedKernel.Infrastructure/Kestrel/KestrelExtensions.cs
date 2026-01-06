#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetCommerce.SharedKernel.Infrastructure.Kestrel;

public static class KestrelExtensions
{
    public static void AddEnterpriseWebHost(this WebApplicationBuilder builder)
    {
        // 1. Hardening Kestrel via Services Configuration
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            options.AddServerHeader = false; // Security: Hide version
            options.AllowResponseHeaderCompression = true;

            // 2025 Best Practice: Support HTTP/3 for performance
            // Kestrel will automatically fallback from HTTP/3 to HTTP/2 if UDP is blocked in corporate networks.
            options.ConfigureEndpointDefaults(o => o.Protocols = HttpProtocols.Http1AndHttp2AndHttp3);

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

        builder.Services.AddRequestTimeouts();

        // Note: Response Compression is already in your Program.cs
    }

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
