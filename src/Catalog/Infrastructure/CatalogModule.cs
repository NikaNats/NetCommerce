using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Application.Categories.Mappers;
using NetCommerce.Catalog.Application.Products.Mappers;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using NetCommerce.Catalog.Infrastructure.Services;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.Ordering.Domain.Orders;

namespace NetCommerce.Catalog.Infrastructure;

/// <summary>
///     Catalog module registration.
/// </summary>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "CatalogDb"
        // Using DbContext pooling for improved performance in high-scale scenarios
        var connectionString = configuration.GetConnectionString("CatalogDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextPool<CatalogDbContext>(options =>
        {
            options.UseNpgsql(connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.Schema);

                    // Enable Connection Resiliency
                    npgsqlOptions.EnableRetryOnFailure(
                        5,
                        TimeSpan.FromSeconds(30),
                        null);
                });
        });

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        // Repositories
        // Product repository with caching decorator for enterprise-scale read performance
        services.AddScoped<ProductRepository>();
        services.AddScoped<IProductRepository>(provider =>
            new CachedProductRepository(
                provider.GetRequiredService<ProductRepository>(),
                provider.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>()));

        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Mappers (DRY/KISS - centralized mapping logic)
        services.AddSingleton<IProductMapper, ProductMapper>();
        services.AddSingleton<ICategoryMapper, CategoryMapper>();

        // Services
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<ICdnUrlGenerator, CdnUrlGenerator>();

        services.AddScoped<IPriceLookupService, OrderingPriceLookup>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
