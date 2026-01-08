#nullable enable
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.Kernel.Security;

/// <summary>
///     Extension methods for registering security kernel services.
/// </summary>
public static class SecurityExtensions
{
    /// <summary>
    ///     Registers HTTP-based tenant context for web applications.
    /// </summary>
    public static IServiceCollection AddKernelHttpTenantContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();
        return services;
    }

    /// <summary>
    ///     Registers system tenant context for background services.
    /// </summary>
    public static IServiceCollection AddKernelSystemTenantContext(this IServiceCollection services, string tenantId)
    {
        services.AddSingleton<ITenantContext>(new SystemTenantContext(tenantId));
        return services;
    }

    /// <summary>
    ///     Registers development tenant context for testing.
    /// </summary>
    public static IServiceCollection AddKernelDevelopmentTenantContext(
        this IServiceCollection services,
        string tenantId = "dev_tenant")
    {
        services.AddSingleton<ITenantContext>(new DevelopmentTenantContext(tenantId));
        return services;
    }

    /// <summary>
    ///     Adds OIDC role claims transformation (Keycloak, Auth0, etc.).
    /// </summary>
    public static IServiceCollection AddOidcRoleClaimsTransformation(
        this IServiceCollection services,
        string? apiClientId = null)
    {
        services.AddSingleton<IClaimsTransformation>(new OidcRoleClaimsTransformation(apiClientId));
        return services;
    }
}
