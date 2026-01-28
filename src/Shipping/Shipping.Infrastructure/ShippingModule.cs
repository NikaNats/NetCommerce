using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Kernel.Application;
using NetCommerce.Shipping.Application.Adapters;
using NetCommerce.Shipping.Application.Repositories;
using NetCommerce.Shipping.Application.Services;
using NetCommerce.Shipping.Infrastructure.Adapters;
using NetCommerce.Shipping.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence.Repositories;
using NetCommerce.Shipping.Infrastructure.Services;

namespace NetCommerce.Shipping.Infrastructure;

/// <summary>
///     Dependency injection extensions for the Shipping module.
/// </summary>
public static class ShippingModule
{
    /// <summary>
    ///     Registers the Shipping module services.
    /// </summary>
    public static IServiceCollection AddShippingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "ShippingDb"
        var connectionString = configuration.GetConnectionString("ShippingDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextPool<ShippingDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", ShippingDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ShippingDbContext>());

        // Repository
        services.AddScoped<IShipmentRepository, ShipmentRepository>();

        // Courier configuration (defaults to MockMode=true for development)
        services.Configure<CourierOptions>(configuration.GetSection(CourierOptions.SectionName));

        // Register courier adapters
        services.AddSingleton<ICourierAdapter, DhlCourierAdapter>();
        services.AddSingleton<ICourierAdapter, FedExCourierAdapter>();

        // Register shipping service
        services.AddScoped<IShippingService, ShippingService>();

        return services;
    }
}
