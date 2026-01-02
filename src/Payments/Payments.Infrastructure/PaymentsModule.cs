using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Gateways;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence.Repositories;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Behaviors;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Payments.Infrastructure;

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

        // Outbox Processor for guaranteed event delivery
        services.AddOutboxProcessor<PaymentsDbContext>(configuration);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PaymentsTransactionBehavior<,>));

        return services;
    }
}

internal class PaymentsTransactionBehavior<TRequest, TResponse>
    : ResilientTransactionBehavior<TRequest, Result<TResponse>, PaymentsDbContext>
    where TRequest : ICommand<TResponse>
{
    public PaymentsTransactionBehavior(
        PaymentsDbContext dbContext,
        ILogger<ResilientTransactionBehavior<TRequest, Result<TResponse>, PaymentsDbContext>> logger)
        : base(dbContext, logger)
    {
    }
}