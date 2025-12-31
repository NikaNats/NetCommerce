using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

namespace NetCommerce.Ordering.Infrastructure;

public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "OrderingDb"
        // Using DbContext pooling for improved performance in high-scale scenarios
        var connectionString = configuration.GetConnectionString("OrderingDb") 
                            ?? configuration.GetConnectionString("DefaultConnection");
        
        services.AddDbContextPool<OrderingDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", OrderingDbContext.Schema)));

        // Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Outbox Processor for guaranteed event delivery
        services.AddOutboxProcessor<OrderingDbContext>(configuration);

        return services;
    }
}

