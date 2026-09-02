#nullable enable
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Compliance.Audit;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Kernel.EfCore;

public static class EfCoreExtensions
{
    /// <summary>
    ///     Registers EF Core with Wolverine transactional outbox integration.
    ///     Domain events are handled via Wolverine's outbox pattern.
    /// </summary>
    public static IServiceCollection AddKernelEfCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureOptions)
        where TContext : BaseDbContext
    {
        // 1. Core Services
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        // AuditRepository demands a DbContext; only concrete bounded contexts are registered, never
        // the base type. Resolve the module's context via factory so DI validation (e.g. Wolverine
        // 'codegen write' container scan) can construct the descriptor.
        services.AddScoped<IAuditRepository>(sp => new AuditRepository(sp.GetRequiredService<TContext>()));
        services.AddScoped<AuditService>();

        // 2. Domain Event Dispatcher (Wolverine bridge) - not needed with outbox
        // services.AddScoped<IDomainEventDispatcher, WolverineEventDispatcher>();

        // 3. Register Interceptors (Scoped) - remove domain event interceptor
        services.AddScoped<TenantSaveInterceptor>();
        services.AddScoped<AuditInterceptor>();
        // services.AddScoped<DomainEventDispatchInterceptor>();

        // 4. Register DbContext with wired-up Interceptors
        services.AddDbContext<TContext>((sp, options) =>
        {
            var tenantInterceptor = sp.GetRequiredService<TenantSaveInterceptor>();
            var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
            // var domainInterceptor = sp.GetRequiredService<DomainEventDispatchInterceptor>();

            // Order matters: Tenant (Data Isolation) -> Audit (Compliance)
            options.AddInterceptors(tenantInterceptor, auditInterceptor);

            // Apply specific provider config (Npgsql, Sqlite, etc.)
            configureOptions(options);
        });

        return services;
    }

    /// <summary>
    ///     Overload for custom Audit Repository.
    /// </summary>
    public static IServiceCollection AddKernelEfCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAuditRepository>(
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
        // services.AddScoped<DomainEventDispatchInterceptor>();

        services.AddDbContext<TContext>((sp, options) =>
        {
            var tenantInterceptor = sp.GetRequiredService<TenantSaveInterceptor>();
            var auditInterceptor = sp.GetRequiredService<AuditInterceptor>();
            // var domainInterceptor = sp.GetRequiredService<DomainEventDispatchInterceptor>();

            options.AddInterceptors(tenantInterceptor, auditInterceptor);
            configureOptions(options);
        });

        return services;
    }
}
