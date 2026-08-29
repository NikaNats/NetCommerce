using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NetCommerce.Inventory.Application.Stock.Mappers;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.BackgroundJobs;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence.Repositories;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.EfCore;

namespace NetCommerce.Inventory.Infrastructure;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - pooled (Inventory high contention: 25). Total 130/pod → 390 for 3 pods → max_connections ≥400
        services.AddPooledKernelDbContext<InventoryDbContext>(configuration, "InventoryDb", maxPoolSize: 25);

        // Repositories
        services.AddScoped<IStockRepository, StockRepository>();

        // Query services for cross-module communication
        services.AddScoped<NetCommerce.Domain.Shared.IStockQueryService, NetCommerce.Inventory.Infrastructure.Services.StockQueryService>();

        // Mappers (DRY/KISS - centralized mapping logic)
        services.AddSingleton<IStockMapper, StockMapper>();

        // Background jobs - health-aware with circuit breaker
        services.AddOptions<ReservationCleanupOptions>()
            .BindConfiguration(ReservationCleanupOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<CleanupJobHealthState>();
        services.AddHealthChecks().AddCheck<CleanupJobHealthCheck>("reservation_cleanup");
        services.AddHostedService<ReservationCleanupJob>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
