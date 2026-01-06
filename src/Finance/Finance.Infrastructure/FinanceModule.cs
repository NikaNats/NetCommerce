using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Infrastructure.Gateways;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Finance.Infrastructure;

/// <summary>
///     Finance Module configuration for dependency injection.
///     Registers all Finance-related services and infrastructure.
/// </summary>
public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        // Domain Services
        services.AddScoped<ReconciliationEngine>();

        // Repositories
        services.AddScoped<IReconciliationSessionRepository, ReconciliationSessionRepository>();

        // Gateways
        services.AddHttpClient<IPaymentGateway, StripePaymentGateway>();

        // Infrastructure
        services.AddDbContext<FinanceDbContext>();

        // Register DbContext as IUnitOfWork for transaction management
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FinanceDbContext>());

        return services;
    }
}
