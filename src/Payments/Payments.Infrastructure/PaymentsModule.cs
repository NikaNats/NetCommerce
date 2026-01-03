using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Gateways;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Payments.Infrastructure;

/// <summary>
///     Payments module registration.
/// </summary>
public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Database - uses Aspire-provided connection string "PaymentsDb"
        // Using DbContext pooling for improved performance in high-scale scenarios
        var connectionString = configuration.GetConnectionString("PaymentsDb")
                               ?? configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextPool<PaymentsDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                b =>
                {
                    b.MigrationsHistoryTable("__EFMigrationsHistory", PaymentsDbContext.Schema);
                    b.EnableRetryOnFailure(5, TimeSpan.FromSeconds(30), null);
                }));

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());

        // Repositories
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();

        // Payment Gateway - Stripe by default
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();

        // Note: Wolverine handles transactional outbox automatically via its middleware.
        // No explicit pipeline behaviors needed - transactions are managed by [AutoApplyTransactions] policy.

        return services;
    }
}
