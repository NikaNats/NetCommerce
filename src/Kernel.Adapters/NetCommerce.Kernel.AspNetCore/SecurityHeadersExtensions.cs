#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NetEscapades.AspNetCore.SecurityHeaders;

namespace NetCommerce.Kernel.AspNetCore;

/// <summary>
/// Configures enterprise-grade HTTP security headers.
/// Centralizes all security header policies to ensure OWASP compliance across all API endpoints.
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Registers security header policies in the DI container.
    /// Must be called during service configuration (builder.Services).
    /// </summary>
    public static IServiceCollection AddNetCommerceSecurityHeaders(this IServiceCollection services)
    {
        var policies = services.AddSecurityHeaderPolicies();
        policies.SetDefaultPolicy(policy =>
        {
            policy.AddDefaultApiSecurityHeaders();
            policy.AddStrictTransportSecurityMaxAgeIncludeSubDomains(maxAgeInSeconds: 31536000);
            policy.RemoveServerHeader();
        });
        policies.AddPolicy("ScalarDevUI", policy =>
        {
            policy.AddDefaultSecurityHeaders();
            policy.AddContentSecurityPolicy(builder =>
            {
                builder.AddDefaultSrc().Self();
                builder.AddScriptSrc().Self().UnsafeInline().UnsafeEval();
                builder.AddStyleSrc().Self().UnsafeInline();
                builder.AddImgSrc().Self().Data().OverHttps();
                builder.AddFontSrc().Self().Data();
                builder.AddConnectSrc().Self();
                builder.AddFrameAncestors().None();
            });
            policy.AddFrameOptionsDeny();
            policy.AddContentTypeOptionsNoSniff();
            policy.AddReferrerPolicyNoReferrer();
        });
        return services;
    }

    /// <summary>
    /// Adds the security headers middleware to the pipeline.
    /// MUST be placed at the very beginning of the middleware pipeline to ensure headers
    /// are applied to all responses, including error pages and static files.
    /// </summary>
    public static IApplicationBuilder UseNetCommerceSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseSecurityHeaders();
    }
}
