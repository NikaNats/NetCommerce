using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using NetCommerce.Kernel.Stripe;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.BackgroundJobs;
using NetCommerce.Payments.Infrastructure.Gateways;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence.Repositories;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.EfCore;
using Polly;

namespace NetCommerce.Payments.Infrastructure;

/// <summary>
///     Payments module registration.
/// </summary>
public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "PaymentsDb"
        // Using AddKernelEfCore for interceptor-based audit & tenant isolation
        var connectionString = configuration.GetConnectionString("PaymentsDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddKernelEfCore<PaymentsDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", PaymentsDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // Repositories
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

        // ============================================================================
        // Shared Stripe Infrastructure (from NetCommerce.Kernel.Stripe)
        // ============================================================================
        services.AddStripeKernel(configuration);

        // ============================================================================
        // Payment Gateway with Production-Ready Circuit Breaker
        // ============================================================================
        // Critical for Go-Live: App stays up even if Stripe is temporarily down
        services.AddHttpClient<IPaymentGateway, StripePaymentGateway>()
            .AddStandardResilienceHandler(options =>
            {
                // Retry: 3 attempts with exponential backoff + jitter
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.Retry.UseJitter = true;
                options.Retry.BackoffType = DelayBackoffType.Exponential;

                // Circuit Breaker: Trip after 50% failure rate in 10s window
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

                // Timeout: Individual request timeout (Stripe recommends 30s)
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);

                // Total request timeout (including retries)
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
            });

        // Background Jobs
        services.AddHostedService<PaymentReconciliationJob>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
