#nullable enable
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Audit;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Kernel.EfCore;

/// <summary>
///     Extension methods for registering EF Core kernel services.
/// </summary>
public static class EfCoreExtensions
{
    /// <summary>
    ///     Registers the universal EF Core kernel services.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    public static IServiceCollection AddKernelEfCore<TContext>(this IServiceCollection services)
        where TContext : BaseDbContext
    {
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<AuditService>();
        services.AddSingleton<AuditInterceptor>();

        return services;
    }

    /// <summary>
    ///     Registers the universal EF Core kernel services with a custom audit repository.
    /// </summary>
    public static IServiceCollection AddKernelEfCore<TContext, TAuditRepository>(this IServiceCollection services)
        where TContext : BaseDbContext
        where TAuditRepository : class, IAuditRepository
    {
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IAuditRepository, TAuditRepository>();
        services.AddScoped<AuditService>();
        services.AddSingleton<AuditInterceptor>();

        return services;
    }
}
