using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NetCommerce.Kernel.AspNetCore;

/// <summary>
/// Extension methods for registering AspNetCore Kernel services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers AspNetCore Kernel services including Problem Details configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKernelAspNetCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register Problem Details options
        services.Configure<ProblemDetailsOptions>(
            configuration.GetSection("ProblemDetails"));

        // Register URI generator as singleton
        services.AddSingleton<ProblemDetailsUriGenerator>();

        return services;
    }

    /// <summary>
    /// Registers AspNetCore Kernel services with custom options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKernelAspNetCore(
        this IServiceCollection services,
        Action<ProblemDetailsOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<ProblemDetailsUriGenerator>();

        return services;
    }
}
