using NetCommerce.Basket.Application;
using NetCommerce.Basket.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

