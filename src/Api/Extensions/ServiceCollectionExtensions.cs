using FluentValidation;
using MediatR;
using NetCommerce.Basket.Infrastructure;
using NetCommerce.Catalog.Infrastructure;
using NetCommerce.Inventory.Infrastructure;
using NetCommerce.Media.Infrastructure;
using NetCommerce.Ordering.Infrastructure;
using NetCommerce.Payments.Infrastructure;
using NetCommerce.SharedKernel.Application.Behaviors;
using NetCommerce.SharedKernel.Infrastructure;
using NetCommerce.SharedKernel.Infrastructure.Redis;

namespace NetCommerce.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add API services for Minimal API (no controllers).
    /// </summary>
    public static IServiceCollection AddApiServicesMinimal(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // CORS
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // Redis services from Aspire-injected IConnectionMultiplexer
        services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
        services.AddSingleton<IIdempotencyService, RedisIdempotencyService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // MediatR with all application assemblies
        services.AddMediatR(cfg => 
        {
            cfg.RegisterServicesFromAssemblies(
                typeof(NetCommerce.Catalog.Application.Products.Commands.CreateProductCommand).Assembly,
                typeof(NetCommerce.Inventory.Application.Stock.Commands.ReserveStockCommand).Assembly,
                typeof(NetCommerce.Ordering.Application.Orders.Commands.CreateOrderCommand).Assembly
            );
        });
        
        // FluentValidation
        services.AddValidatorsFromAssemblyContaining<NetCommerce.Catalog.Application.Products.Validators.CreateProductCommandValidator>();
        
        // MediatR Pipeline Behaviors
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

    public static IServiceCollection AddModules(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Register all modules (Identity is now handled by Keycloak)
        services.AddCatalogModule(configuration);
        services.AddBasketModule(configuration);
        services.AddOrderingModule(configuration);
        services.AddInventoryModule(configuration);
        services.AddPaymentsModule(configuration);
        services.AddMediaModule(configuration);

        return services;
    }
}
