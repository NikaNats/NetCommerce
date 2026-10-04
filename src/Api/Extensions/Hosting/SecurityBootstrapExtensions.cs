using System.IO.Compression;
using Microsoft.AspNetCore.DataProtection;
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
        // Networks and hop limit come from configuration (ForwardedHeaders section)
        // so a multi-hop prod path (CDN -> ALB -> pod) can declare its VPC CIDRs
        // and ForwardLimit without a code change. XForwardedHost is deliberately
        // NOT forwarded: absolute URLs the app emits are relative (e.g. the order
        // Location header) or operator-declared (frontend PUBLIC_ORIGIN), so Host
        // forwarding would only add host-header poisoning surface.
        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            var knownNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()
                ?? ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];
            foreach (var cidr in knownNetworks.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                // Invalid CIDR fails startup: a typo here silently untrusts the
                // proxy chain and collapses rate limiting onto proxy IPs.
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr.Trim()));
            }
            options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;
        });

        // Enterprise HTTP Security Headers (OWASP Compliant)
        builder.Services.AddNetCommerceSecurityHeaders();

        // Enterprise-Hardened Web Host (Kestrel Security & Performance)
        builder.AddEnterpriseWebHost();

        // Defense in Depth: Antiforgery Protection
        builder.Services.AddAntiforgery();

        // Data Protection key ring — shared across replicas via Redis.
        // Default file-system keys break antiforgery/cookies on >1 replica and
        // on every restart. Redis is already required infrastructure (basket
        // repository), so outside Development/Testing a missing OR unreachable
        // Redis fails fast here rather than silently downgrading to per-pod
        // keys. Development/Testing warn and continue with ephemeral keys:
        // local runs frequently start before their Redis container, and test
        // hosts are single in-process servers where ephemeral keys are correct.
        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        var allowEphemeralKeys = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        StackExchange.Redis.IConnectionMultiplexer? redis = null;
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            if (!allowEphemeralKeys)
            {
                throw new InvalidOperationException(
                    "Missing connection string: Redis (required for the shared Data Protection key ring).");
            }
            Console.WriteLine("WARNING: ConnectionStrings__Redis is not set; using ephemeral file-system Data Protection keys (single replica only).");
        }
        else
        {
            try
            {
                redis = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString);
            }
            catch (Exception ex) when (ex is StackExchange.Redis.RedisConnectionException
                || ex is TimeoutException
                || ex is System.Net.Sockets.SocketException
                || ex is System.IO.IOException)
            {
                if (!allowEphemeralKeys)
                {
                    throw new InvalidOperationException(
                        "ConnectionStrings__Redis is configured but unreachable (required for the shared Data Protection key ring).", ex);
                }
                Console.WriteLine(
                    $"WARNING: Redis is unreachable ({ex.GetType().Name}); using ephemeral file-system Data Protection keys until it returns.");
            }
        }
        if (redis is not null)
        {
            builder.Services.AddDataProtection()
                .SetApplicationName("NetCommerce-Api")
                .PersistKeysToStackExchangeRedis(redis, "NetCommerce-DataProtection-Keys");
        }

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
