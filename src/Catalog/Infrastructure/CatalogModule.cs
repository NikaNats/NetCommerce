using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Application.Categories.Mappers;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Application.Products.Mappers;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using NetCommerce.Catalog.Infrastructure.Services;
using NetCommerce.SharedKernel.Domain;

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
        // DbContext - uses Aspire-provided connection string "CatalogDb"
        // Using DbContext pooling for improved performance in high-scale scenarios
        var connectionString = configuration.GetConnectionString("CatalogDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextPool<CatalogDbContext>(options =>
        {
            options.UseNpgsql(connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.Schema);
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
                provider.GetRequiredService<IDistributedCache>()
            ));

        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Mappers (DRY/KISS - centralized mapping logic)
        services.AddSingleton<IProductMapper, ProductMapper>();
        services.AddSingleton<ICategoryMapper, CategoryMapper>();

        // Services
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<ICdnUrlGenerator, CdnUrlGenerator>();

        // MediatR handlers from Application assembly
        services.AddMediatR(cfg => { cfg.RegisterServicesFromAssembly(typeof(CreateProductCommand).Assembly); });

        // FluentValidation validators
        services.AddValidatorsFromAssembly(typeof(CreateProductCommand).Assembly);

        return services;
    }
}