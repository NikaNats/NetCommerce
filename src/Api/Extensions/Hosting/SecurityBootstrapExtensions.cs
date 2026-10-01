using System.IO.Compression;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.Api.Extensions.Hosting;

/// <summary>
///     Enterprise security bootstrap: reverse-proxy awareness, OWASP headers, hardened
///     Kestrel host, antiforgery, Zero-Trust Keycloak auth with all five RBAC policies,
///     and response compression. Extracted verbatim from Program.cs — every policy and
///     every option value is unchanged (notably the OwnerOnly policy, which must stay).
/// </summary>
public static class SecurityBootstrapExtensions
{
    public static WebApplicationBuilder AddEnterpriseSecurity(this WebApplicationBuilder builder)
    {
        // Reverse Proxy Awareness (Forwarded Headers) - MUST be before RateLimiter/Auth.
        // CRITICAL SECURITY: Trust only KnownNetworks/KnownProxies. Never trust X-Forwarded-For from open internet.
        // Behind ALB/Nginx/Cloudflare, RemoteIpAddress without this collapses all clients to proxy IP.
        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            // Default private ranges – override via configuration for production VPC CIDRs
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        });

        // Enterprise HTTP Security Headers (OWASP Compliant)
        builder.Services.AddNetCommerceSecurityHeaders();

        // Enterprise-Hardened Web Host (Kestrel Security & Performance)
        builder.AddEnterpriseWebHost();

        // Defense in Depth: Antiforgery Protection
        builder.Services.AddAntiforgery();

        // Zero-Trust Authentication with Keycloak (Identity Mesh).
        // Replaces manual JWT configuration with a standardized Zero-Trust security stack:
        // JWT Bearer with strict validation, Keycloak role claim transformation, optional token
        // introspection kill switch, and token exchange for downstream service calls.
        builder.AddZeroTrustAuthentication();

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy => policy.RequireRole("admin"))
            .AddPolicy("VendorOnly", policy => policy.RequireRole("admin", "vendor"))
            .AddPolicy("CustomerOnly", policy => policy.RequireRole("customer"))
            .AddPolicy("OwnerOnly", policy =>
                policy.Requirements.Add(new NetCommerce.Kernel.Security.Authorization.ResourceOwnerRequirement()))
            .AddPolicy("AdminElevated", policy =>
            {
                policy.RequireRole("admin", "Admin");
                policy.Requirements.Add(new NetCommerce.Kernel.Security.Authorization.AdminElevatedRequirement());
            });

        // Response Compression (Brotli + Gzip)
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                ["application/json", "application/problem+json"]);
        });

        builder.Services.Configure<BrotliCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; });

        builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.SmallestSize;
        });

        return builder;
    }
}
