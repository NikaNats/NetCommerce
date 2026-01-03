using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.BackgroundJobs;
using NetCommerce.Ordering.Infrastructure.Metrics;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Infrastructure;

/// <summary>
///     Ordering module registration.
/// </summary>
public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "OrderingDb"
        // Using DbContext pooling for improved performance in high-scale scenarios
        var connectionString = configuration.GetConnectionString("OrderingDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextPool<OrderingDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", OrderingDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());

        // Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Grace Period configuration and background service
        services.Configure<GracePeriodOptions>(configuration.GetSection(GracePeriodOptions.SectionName));
        services.AddHostedService<GracePeriodManagerService>();

        // ============================================================================
        // Metrics & Observability
        // ============================================================================
        // Register the metrics singleton (provides ObservableGauge for saga states)
        services.AddSingleton<OrderingMetrics>();

        // Background service that polls the database to update metrics
        services.AddHostedService<SagaMonitorService>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
