using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using Polly.CircuitBreaker;
using Polly.Retry;
using Stripe;

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
        // Payment Gateway with Production-Ready Circuit Breaker (AOT-compatible)
        // ============================================================================
        // NOTE: AddHttpClient<IPaymentGateway, StripePaymentGateway> does NOT apply here
        // because StripePaymentGateway uses the Stripe .NET SDK directly, not HttpClient.
        // We build a ResiliencePipeline and inject it as a singleton so the circuit-breaker
        // state is shared across all requests in the process.
        var stripeResiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                // Only retry transient Stripe errors: rate-limit, 503, 529, 504
                ShouldHandle = new PredicateBuilder().Handle<StripeException>(ex => ex.IsTransient()),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                // Trip after 50% failure rate across a 30-second sliding window (min 5 calls)
                ShouldHandle = new PredicateBuilder().Handle<StripeException>(ex => ex.IsTransient()),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .Build();

        services.AddSingleton(stripeResiliencePipeline);
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();

        // Background Jobs
        services.AddHostedService<PaymentReconciliationJob>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
