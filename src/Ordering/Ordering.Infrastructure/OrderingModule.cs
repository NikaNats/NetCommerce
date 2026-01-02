using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.BackgroundJobs;
using NetCommerce.Ordering.Infrastructure.Outbox;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Behaviors;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;
using NetCommerce.SharedKernel.Results;

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
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", OrderingDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());

        // Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();

        // Outbox Processor for guaranteed event delivery
        services.AddOutboxProcessor<OrderingDbContext>(configuration);

        // Dead-letter handler for compensating actions when outbox messages exhaust retries
        services.AddScoped<IOutboxDeadLetterHandler<OrderingDbContext>, OrderingOutboxDeadLetterHandler>();

        // Grace Period configuration and background service
        services.Configure<GracePeriodOptions>(configuration.GetSection(GracePeriodOptions.SectionName));
        services.AddHostedService<GracePeriodManagerService>();

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OrderingTransactionBehavior<,>));

        return services;
    }
}

internal class OrderingTransactionBehavior<TRequest, TResponse>
    : ResilientTransactionBehavior<TRequest, Result<TResponse>, OrderingDbContext>
    where TRequest : ICommand<TResponse>
{
    public OrderingTransactionBehavior(
        OrderingDbContext dbContext,
        ILogger<ResilientTransactionBehavior<TRequest, Result<TResponse>, OrderingDbContext>> logger)
        : base(dbContext, logger)
    {
    }
}