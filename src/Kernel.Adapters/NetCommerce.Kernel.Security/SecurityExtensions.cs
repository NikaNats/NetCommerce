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
    ///     Registers HTTP-based user context for web applications.
    /// </summary>
    public static IServiceCollection AddKernelHttpUserContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpUserContext>();
        return services;
    }

    /// <summary>
    ///     Registers system user context for background services.
    /// </summary>
    public static IServiceCollection AddKernelSystemUserContext(this IServiceCollection services, string? systemIdentifier = null)
    {
        services.AddSingleton<IUserContext>(new SystemUserContext(systemIdentifier));
        return services;
    }

    /// <summary>
    ///     Registers development user context for testing.
    /// </summary>
    public static IServiceCollection AddKernelDevelopmentUserContext(
        this IServiceCollection services,
        string userId = "dev_user",
        string role = "Admin")
    {
        services.AddSingleton<IUserContext>(new DevelopmentUserContext(userId, role));
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
