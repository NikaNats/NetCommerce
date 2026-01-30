using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Ordering.Application.Orders.Services;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.BackgroundJobs;
using NetCommerce.Ordering.Infrastructure.Metrics;
using NetCommerce.Ordering.Infrastructure.Notifications;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence.Repositories;
using NetCommerce.Ordering.Infrastructure.Services;
using NetCommerce.Kernel.Application.Notifications;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Ordering.Infrastructure;

/// <summary>
///     Ordering module registration.
/// </summary>
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

        // ============================================================================
        // Triple-Pass Pricing Services
        // ============================================================================
        // Tax Provider - using local fallback for resilience
        services.AddScoped<ITaxProvider, LocalTaxProvider>();

        // Promotion Engine - simple implementation (can be replaced with external service)
        services.AddScoped<IPromotionEngine, SimplePromotionEngine>();

        // ============================================================================
        // Notification Services (Event-Driven Notification Sidecar Pattern)
        // ============================================================================
        // Template Engine - simple implementation (replace with Razor/Scriban in production)
        services.AddSingleton<ITemplateEngine, SimpleTemplateEngine>();

        // Email Provider - using in-memory for development/testing
        // Production: Replace with SendGridEmailProvider or AwsSesEmailProvider
        // services.AddHttpClient<IEmailProvider, SendGridEmailProvider>()
        //     .AddStandardResilienceHandler(options => {
        //         options.Retry.MaxRetryAttempts = 3;
        //         options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        //         options.CircuitBreaker.FailureRatio = 0.5; // Trip if 50% of calls fail
        //         options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        //     });
        services.AddSingleton<IEmailProvider, InMemoryEmailProvider>();

        // Wolverine will auto-discover OrderNotificationHandler as it's decorated with [WolverineHandler]

        // Grace Period configuration and background service
        services.Configure<GracePeriodOptions>(configuration.GetSection(GracePeriodOptions.SectionName));
        services.AddHostedService<GracePeriodManagerService>();

        // ============================================================================
        // Metrics & Observability
        // ============================================================================
        // Register the metrics singleton (provides ObservableGauge for saga states)
        services.AddSingleton<OrderingMetrics>();

        // Background service that polls the database to update metrics
        services.AddHostedService<SagaMonitorService>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
