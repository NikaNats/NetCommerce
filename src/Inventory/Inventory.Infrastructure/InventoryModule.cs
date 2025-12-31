using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Inventory.Application.Stock.Mappers;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.BackgroundJobs;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence.Repositories;

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
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema)));

        // Repositories
        services.AddScoped<IStockRepository, StockRepository>();

        // Mappers (DRY/KISS - centralized mapping logic)
        services.AddSingleton<IStockMapper, StockMapper>();

        // Background jobs
        services.AddOptions<ReservationCleanupOptions>()
            .BindConfiguration(ReservationCleanupOptions.SectionName)
            .ValidateOnStart();

        services.AddHostedService<ReservationCleanupJob>();

        return services;
    }
}

