using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Audit;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Domain.Webhooks;
using NetCommerce.Finance.Infrastructure.Gateways;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence.Repositories;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.EfCore;
using NetCommerce.Kernel.Stripe;

namespace NetCommerce.Finance.Infrastructure;

/// <summary>
///     Finance Module configuration for dependency injection.
///     Registers all Finance-related services and infrastructure.
/// </summary>
public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        // ============================================================================
        // Domain Services
        // ============================================================================
        services.AddScoped<ReconciliationEngine>();

        // ============================================================================
        // Repositories
        // ============================================================================
        services.AddScoped<IReconciliationSessionRepository, ReconciliationSessionRepository>();
        services.AddScoped<IWebhookEventStore, WebhookEventStore>();
        services.AddScoped<IFinancialAuditRepository, FinancialAuditRepository>();

        // ============================================================================
        // Alerting Configuration
        // ============================================================================
        services.Configure<AlertingOptions>(configuration.GetSection("Finance:Alerting"));

        // ============================================================================
        // Shared Stripe Infrastructure (from NetCommerce.Kernel.Stripe)
        // ============================================================================
        // Note: AddStripeKernel may already be called by Payments module, but is idempotent
        services.AddStripeKernel(configuration);

        // ============================================================================
        // Reconciliation Gateway using Stripe SDK
        // ============================================================================
        // Uses StripeClientFactory for proper connection pooling and resilience
        services.AddScoped<IPaymentGateway, StripeReconciliationGateway>();

        // ============================================================================
        // HTTP Client for PagerDuty/External Alerting
        // ============================================================================
        services.AddHttpClient("PagerDuty", client =>
        {
            client.BaseAddress = new Uri("https://events.pagerduty.com/v2/");
            client.Timeout = TimeSpan.FromSeconds(10);
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
