using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Inventory.Application.Stock.Mappers;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.BackgroundJobs;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence.Repositories;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Inventory.Infrastructure;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "InventoryDb"
        // Using DbContext pooling for improved performance in high-scale scenarios
        var connectionString = configuration.GetConnectionString("InventoryDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextPool<InventoryDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());

        // Repositories
        services.AddScoped<IStockRepository, StockRepository>();

        // Mappers (DRY/KISS - centralized mapping logic)
        services.AddSingleton<IStockMapper, StockMapper>();

        // Background jobs
        services.AddOptions<ReservationCleanupOptions>()
            .BindConfiguration(ReservationCleanupOptions.SectionName)
            .ValidateOnStart();

        services.AddHostedService<ReservationCleanupJob>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
