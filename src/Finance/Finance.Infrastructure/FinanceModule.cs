using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Infrastructure.Gateways;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence.Repositories;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.EfCore;
using Polly;

namespace NetCommerce.Finance.Infrastructure;

/// <summary>
///     Finance Module configuration for dependency injection.
///     Registers all Finance-related services and infrastructure.
/// </summary>
public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Domain Services
        services.AddScoped<ReconciliationEngine>();

        // Repositories
        services.AddScoped<IReconciliationSessionRepository, ReconciliationSessionRepository>();

        // ============================================================================
        // Payment Gateway with Production-Ready Circuit Breaker
        // ============================================================================
        // Critical for Go-Live: System stays up even if Stripe/PSP is down
        services.AddHttpClient<IPaymentGateway, StripePaymentGateway>()
            .AddStandardResilienceHandler(options =>
            {
                // Retry: 3 attempts with exponential backoff
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.Retry.UseJitter = true;
                options.Retry.BackoffType = DelayBackoffType.Exponential;

                // Circuit Breaker: Trip after 50% failure rate in 10s window
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 5; // Need 5 requests before tripping
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

                // Timeout: Individual request timeout
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);

                // Total request timeout (including retries)
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
            });

        // Database - uses AddKernelEfCore for interceptor-based audit & tenant isolation
        var connectionString = configuration.GetConnectionString("FinanceDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddKernelEfCore<FinanceDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        return services;
    }
}
