using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Shipping.Application.Adapters;
using NetCommerce.Shipping.Application.Services;
using NetCommerce.Shipping.Infrastructure.Adapters;
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
    public static IServiceCollection AddShippingModule(this IServiceCollection services)
    {
        // Register courier adapters
        services.AddSingleton<ICourierAdapter, DhlCourierAdapter>();
        services.AddSingleton<ICourierAdapter, FedExCourierAdapter>();

        // Register shipping service
        services.AddScoped<IShippingService, ShippingService>();

        // TODO: Add repository registrations
        // services.AddScoped<IShipmentRepository, ShipmentRepository>();

        return services;
    }
}
