#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Audit;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Kernel.EfCore;

public static class EfCoreExtensions
{
    /// <summary>
    ///     Registers EF Core with all Kernel interceptors (Audit, Tenant, DomainEvents)
    ///     and allows configuring the DB Provider options.
    /// </summary>
    public static IServiceCollection AddKernelEfCore<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureOptions)
        where TContext : BaseDbContext
    {
        // 1. Core Services
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<AuditService>();

        // 2. Register Interceptors (Scoped)
        services.AddScoped<TenantSaveInterceptor>();
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();

        // 3. Register DbContext with wired-up Interceptors
        services.AddDbContext<TContext>((sp, options) =>
        {
            var tenantInterceptor = sp.GetRequiredService<TenantSaveInterceptor>();
            var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
            var domainInterceptor = sp.GetRequiredService<DomainEventDispatchInterceptor>();

            // Order matters: Tenant (Data Isolation) -> Audit (Compliance) -> Domain Events (Side Effects)
            options.AddInterceptors(tenantInterceptor, auditInterceptor, domainInterceptor);

            // Apply specific provider config (Npgsql, Sqlite, etc.)
            configureOptions(options);
        });

        return services;
    }

    /// <summary>
    ///     Overload for custom Audit Repository.
    /// </summary>
    public static IServiceCollection AddKernelEfCore<TContext, TAuditRepository>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureOptions)
        where TContext : BaseDbContext
        where TAuditRepository : class, IAuditRepository
    {
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IAuditRepository, TAuditRepository>();
        services.AddScoped<AuditService>();

        services.AddScoped<TenantSaveInterceptor>();
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();

        services.AddDbContext<TContext>((sp, options) =>
        {
            var tenantInterceptor = sp.GetRequiredService<TenantSaveInterceptor>();
            var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
            var domainInterceptor = sp.GetRequiredService<DomainEventDispatchInterceptor>();

            options.AddInterceptors(tenantInterceptor, auditInterceptor, domainInterceptor);
            configureOptions(options);
        });

        return services;
    }
}
