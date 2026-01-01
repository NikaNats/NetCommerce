using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Outbox;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Domain;
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

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());

        // Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Outbox Processor for guaranteed event delivery
        services.AddOutboxProcessor<OrderingDbContext>(configuration);

        // Dead-letter handler for compensating actions when outbox messages exhaust retries
        services.AddScoped<IOutboxDeadLetterHandler<OrderingDbContext>, OrderingOutboxDeadLetterHandler>();

        return services;
    }
}

