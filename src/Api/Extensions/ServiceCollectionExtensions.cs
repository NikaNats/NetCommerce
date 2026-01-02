using FluentValidation;
using MediatR;
using NetCommerce.Basket.Infrastructure;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Application.Products.Validators;
using NetCommerce.Catalog.Infrastructure;
using NetCommerce.Inventory.Application.EventHandlers;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Infrastructure;
using NetCommerce.Media.Infrastructure;
using NetCommerce.Ordering.Application.EventHandlers;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Infrastructure;
using NetCommerce.Payments.Infrastructure;
using NetCommerce.SharedKernel.Application.Behaviors;
using NetCommerce.SharedKernel.Infrastructure;
using NetCommerce.SharedKernel.Infrastructure.Redis;

namespace NetCommerce.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Add API services for Minimal API (no controllers).
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

        // MediatR with all application assemblies (includes event handlers for cross-module communication)
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(
                typeof(CreateProductCommand).Assembly,
                typeof(ReserveStockCommand).Assembly,
                typeof(OrderPaidIntegrationEventHandler).Assembly,
                typeof(CreateOrderCommand).Assembly,
                typeof(PaymentCompletedIntegrationEventHandler).Assembly
            );
        });

        // FluentValidation
        services.AddValidatorsFromAssemblyContaining<CreateProductCommandValidator>();

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