using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Infrastructure.Gateways;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence.Repositories;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.EfCore;

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

        // Gateways
        services.AddHttpClient<IPaymentGateway, StripePaymentGateway>();

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
