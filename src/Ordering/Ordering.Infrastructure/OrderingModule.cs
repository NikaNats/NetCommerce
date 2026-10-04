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
using NetCommerce.Kernel.EfCore;

namespace NetCommerce.Ordering.Infrastructure;

/// <summary>
///     Ordering module registration.
/// </summary>
public static class OrderingModule
{
    public static IServiceCollection AddOrderingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - pooled strict limits (Ordering: 20). See NpgsqlPoolingExtensions sizing formula.
        services.AddPooledKernelDbContext<OrderingDbContext>(configuration, "OrderingDb", maxPoolSize: 20);

        // Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();

        // PEAA Gateway: Wolverine saga store is an external resource — centralize
        // its record access behind an application-owned boundary.
        services.AddScoped<ISagaStateGateway, WolverineSagaStateGateway>();

        // ============================================================================
        // Triple-Pass Pricing Services
        // ============================================================================
        // Tax tables and coupon tables are config-overridable and must be
        // explicitly acknowledged for Production-like environments (validators
        // fail startup otherwise — silent wrong-money math is not shippable).
        services.Configure<TaxTableOptions>(configuration.GetSection(TaxTableOptions.SectionName));
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<TaxTableOptions>, TaxTableOptionsValidator>();
        services.AddOptions<TaxTableOptions>().ValidateOnStart();

        services.Configure<PromotionOptions>(configuration.GetSection(PromotionOptions.SectionName));
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<PromotionOptions>, PromotionOptionsValidator>();
        services.AddOptions<PromotionOptions>().ValidateOnStart();

        // Tax Provider - using local fallback for resilience
        services.AddScoped<ITaxProvider>(sp =>
            new LocalTaxProvider(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TaxTableOptions>>().Value));

        // Promotion Engine - simple implementation (can be replaced with external service)
        services.AddScoped<IPromotionEngine>(sp =>
            new SimplePromotionEngine(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PromotionOptions>>().Value));

        // ============================================================================
        // Notification Services (Event-Driven Notification Sidecar Pattern)
        // ============================================================================
        // Template Engine - simple implementation (replace with Razor/Scriban in production)
        services.AddSingleton<ITemplateEngine, SimpleTemplateEngine>();

        // Email Provider - selected via Ordering:Email:Provider.
        // Production-like environments fail startup on InMemory unless explicitly
        // acknowledged (validator); SendGrid requires an API key + sender.
        services.Configure<EmailProviderOptions>(configuration.GetSection(EmailProviderOptions.SectionName));
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<EmailProviderOptions>, EmailProviderOptionsValidator>();
        services.AddOptions<EmailProviderOptions>().ValidateOnStart();

        services.AddHttpClient("SendGrid", client =>
        {
            client.BaseAddress = new Uri("https://api.sendgrid.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        var emailProviderChoice = configuration.GetSection(EmailProviderOptions.SectionName).Get<EmailProviderOptions>()
            ?? new EmailProviderOptions();

        if (string.Equals(emailProviderChoice.Provider, "SendGrid", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailProvider, SendGridEmailProvider>();
        }
        else
        {
            services.AddSingleton<IEmailProvider, InMemoryEmailProvider>();
        }

        // Wolverine will auto-discover OrderNotificationHandler as it's decorated with [WolverineHandler]

        // Grace Period configuration and background service
        services.Configure<GracePeriodOptions>(configuration.GetSection(GracePeriodOptions.SectionName));
        services.AddHostedService<GracePeriodManagerService>();

        // Stuck-saga alerting: pages on-call when money is captured but the
        // saga parks in ManualInterventionRequired (refund failed). Uses the
        // same PagerDuty Events API client shape as the Finance module.
        services.Configure<StuckSagaAlertOptions>(configuration.GetSection(StuckSagaAlertOptions.SectionName));
        services.AddHttpClient("PagerDuty", client =>
        {
            client.BaseAddress = new Uri("https://events.pagerduty.com/v2/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddHostedService<StuckSagaAlertService>();

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
