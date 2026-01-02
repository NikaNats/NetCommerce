using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Inventory.Application.Stock.Mappers;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.BackgroundJobs;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Behaviors;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Inventory.Infrastructure;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "InventoryDb"
        // Using DbContext pooling for improved performance in high-scale scenarios
        var connectionString = configuration.GetConnectionString("InventoryDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextPool<InventoryDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());

        // Repositories
        services.AddScoped<IStockRepository, StockRepository>();

        // Mappers (DRY/KISS - centralized mapping logic)
        services.AddSingleton<IStockMapper, StockMapper>();

        // Background jobs
        services.AddOptions<ReservationCleanupOptions>()
            .BindConfiguration(ReservationCleanupOptions.SectionName)
            .ValidateOnStart();

        services.AddHostedService<ReservationCleanupJob>();

        // Outbox Processor for guaranteed event delivery
        services.AddOutboxProcessor<InventoryDbContext>(configuration);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InventoryTransactionBehavior<,>));

        return services;
    }
}

internal class InventoryTransactionBehavior<TRequest, TResponse>
    : ResilientTransactionBehavior<TRequest, Result<TResponse>, InventoryDbContext>
    where TRequest : ICommand<TResponse>
{
    public InventoryTransactionBehavior(
        InventoryDbContext dbContext,
        ILogger<ResilientTransactionBehavior<TRequest, Result<TResponse>, InventoryDbContext>> logger)
        : base(dbContext, logger)
    {
    }
}