// =============================================================================
// DEPRECATED: Use NetCommerce.Kernel.Security.Authentication extensions
// This file forwards to the canonical implementation in Kernel.Security.
// =============================================================================
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KernelAuth = NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

/// <summary>
///     DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions instead.
///     This class forwards to the canonical implementation in Kernel.Security.
/// </summary>
[Obsolete("Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions instead.")]
public static class ZeroTrustAuthenticationExtensions
{
    /// <summary>
    ///     DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions.AddZeroTrustAuthentication instead.
    /// </summary>
    [Obsolete("Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions.AddZeroTrustAuthentication instead.")]
    public static IHostApplicationBuilder AddZeroTrustAuthentication(
        this IHostApplicationBuilder builder,
        Action<ZeroTrustAuthOptions>? configureOptions = null)
    {
        // Forward to Kernel.Security implementation
        // Note: configureOptions uses legacy type, so we need to adapt
        return KernelAuth.ZeroTrustAuthenticationExtensions.AddZeroTrustAuthentication(builder, opts =>
        {
            if (configureOptions is not null)
            {
                var legacyOpts = new ZeroTrustAuthOptions
                {
                    Authority = opts.Authority,
                    Realm = opts.Realm,
                    Audience = opts.Audience,
                    ApiScope = opts.ApiScope,
                    ClientId = opts.ClientId,
                    ClientSecret = opts.ClientSecret,
                    IntrospectionEnabled = opts.IntrospectionEnabled,
                    IntrospectionCacheSeconds = opts.IntrospectionCacheSeconds,
                    TokenExchangeEnabled = opts.TokenExchangeEnabled
                };
#pragma warning disable CS0618
                configureOptions(legacyOpts);
#pragma warning restore CS0618
                opts.Authority = legacyOpts.Authority;
                opts.Realm = legacyOpts.Realm;
                opts.Audience = legacyOpts.Audience;
                opts.ApiScope = legacyOpts.ApiScope;
                opts.ClientId = legacyOpts.ClientId;
                opts.ClientSecret = legacyOpts.ClientSecret;
                opts.IntrospectionEnabled = legacyOpts.IntrospectionEnabled;
                opts.IntrospectionCacheSeconds = legacyOpts.IntrospectionCacheSeconds;
                opts.TokenExchangeEnabled = legacyOpts.TokenExchangeEnabled;
            }
        });
    }

    /// <summary>
    ///     DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions.UseZeroTrustMiddleware instead.
    /// </summary>
    [Obsolete("Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions.UseZeroTrustMiddleware instead.")]
    public static IApplicationBuilder UseZeroTrustMiddleware(this IApplicationBuilder app)
    {
        return KernelAuth.ZeroTrustAuthenticationExtensions.UseZeroTrustMiddleware(app);
    }

    /// <summary>
    ///     DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions.AddTokenExchange instead.
    /// </summary>
    [Obsolete("Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthenticationExtensions.AddTokenExchange instead.")]
    public static IHttpClientBuilder AddTokenExchange(
        this IHttpClientBuilder builder,
        string targetAudience)
    {
        return KernelAuth.ZeroTrustAuthenticationExtensions.AddTokenExchange(builder, targetAudience);
    }
}
