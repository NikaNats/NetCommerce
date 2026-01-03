using FluentValidation;
using NetCommerce.Basket.Infrastructure;
using NetCommerce.Catalog.Application.Products.Validators;
using NetCommerce.Catalog.Infrastructure;
using NetCommerce.Inventory.Infrastructure;
using NetCommerce.Media.Infrastructure;
using NetCommerce.Ordering.Infrastructure;
using NetCommerce.Payments.Infrastructure;
using NetCommerce.SharedKernel.Infrastructure;

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

        // Date/Time provider for testability
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // FluentValidation - Wolverine uses this via WolverineFx.FluentValidation middleware
        services.AddValidatorsFromAssemblyContaining<CreateProductCommandValidator>();

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
