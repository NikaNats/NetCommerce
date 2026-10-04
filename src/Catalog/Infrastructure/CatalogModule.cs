using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Application.Categories.Mappers;
using NetCommerce.Catalog.Application.Products.Mappers;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Caching;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using NetCommerce.Catalog.Infrastructure.Services;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.EfCore;

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
        // Database - pooled to prevent max_connections exhaustion (Catalog read-heavy: 30)
        // Sizing: 6 contexts × pooled avg 20 + burst = 130 per pod, 3 pods = 390 → set max_connections ≥400 or use PgBouncer
        services.AddPooledKernelDbContext<CatalogDbContext>(configuration, "CatalogDb", maxPoolSize: 30);

        // Repositories
        // Product repository with caching decorator for enterprise-scale read performance
        services.AddScoped<ProductRepository>();
        services.AddScoped<IProductRepository>(provider =>
            new CachedProductRepository(
                provider.GetRequiredService<ProductRepository>(),
                provider.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>()));

        // Read-only view of the SAME cached repository.
        //
        // Registered so other bounded contexts can resolve a product without taking a
        // dependency on Catalog's own abstraction or gaining the ability to mutate an
        // aggregate. The basket pricing adapter reads through this: a basket request
        // must never be able to write to a product.
        services.AddScoped<IReadOnlyRepository<Product, Guid>>(provider =>
            (IReadOnlyRepository<Product, Guid>)provider.GetRequiredService<IProductRepository>());

#pragma warning disable EXTEXP0018 // HybridCache serializer API is experimental in this SDK band
        // Product aggregates are not STJ-deserializable by design; serialize via
        // snapshot DTO instead. Without this, every HybridCache read throws
        // NotSupportedException (HybridCache serializes even the L1/stampede path).
        services.AddHybridCache().AddSerializer<Product?>(new ProductCacheSerializer());
#pragma warning restore EXTEXP0018

        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Mappers (DRY/KISS - centralized mapping logic)
        services.AddSingleton<IProductMapper, ProductMapper>();
        services.AddSingleton<ICategoryMapper, CategoryMapper>();

        // Services
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<ICdnUrlGenerator, CdnUrlGenerator>();

        services.AddScoped<IPriceLookupService, OrderingPriceLookup>();

        // Search-index rebuild (admin disaster recovery). Scoped: shares the
        // request's DbContext and Wolverine outbox transaction boundary.
        //
        // Gated on the SAME condition as the MeilisearchClient registration in
        // InfrastructureExtensions.cs:56. Registering this unconditionally made
        // DI validation fail with
        //   "Unable to resolve service for type 'Meilisearch.MeilisearchClient'
        //    while attempting to activate SearchIndexRebuildService"
        // whenever Meilisearch was not configured — including during
        // `dotnet run -- codegen write` in the AOT Docker build, which has no
        // connection string and no running Meilisearch. That failed the image
        // build at step 10/11 with exit code 134 (SIGABRT from Host.Build()).
        //
        // A consumer asking for ISearchIndexRebuilder in that configuration gets
        // a clear resolution failure naming the missing connection string, which
        // is the intended signal: search rebuild is a Meilisearch-only operation.
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("meilisearch")))
        {
            services.AddScoped<ISearchIndexRebuilder, SearchIndexRebuildService>();
        }

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
