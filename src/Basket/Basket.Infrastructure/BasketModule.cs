using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Basket.Application;

namespace NetCommerce.Basket.Infrastructure;

public static class BasketModule
{
    public static IServiceCollection AddBasketModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Redis is already configured in SharedKernel.Infrastructure
        // Register basket repository
        services.AddScoped<IBasketRepository, RedisBasketRepository>();

        return services;
    }
}