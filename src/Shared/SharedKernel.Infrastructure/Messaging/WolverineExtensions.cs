using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.Postgresql;

namespace NetCommerce.SharedKernel.Infrastructure.Messaging;

/// <summary>
///     Wolverine configuration extensions for the modular monolith.
///     Implements the Transactional Outbox pattern with EF Core and PostgreSQL.
/// </summary>
public static class WolverineExtensions
{
    /// <summary>
    ///     Adds Wolverine as the message bus with transactional outbox support.
    ///     Replaces MediatR with a production-ready, durable messaging infrastructure.
    /// </summary>
    public static IHostBuilder UseWolverineMessaging(
        this IHostBuilder hostBuilder,
        IConfiguration configuration,
        params Type[] handlerAssemblyMarkerTypes)
    {
        var connectionString = configuration.GetConnectionString("postgres")
                               ?? configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Database connection string not found.");

        return hostBuilder.UseWolverine(opts =>
        {
            // ============================================================================
            // Message Persistence with PostgreSQL (Transactional Outbox)
            // ============================================================================
            opts.PersistMessagesWithPostgresql(connectionString, "wolverine");

            // ============================================================================
            // Entity Framework Core Integration
            // ============================================================================
            // Auto-enrolls DbContext transactions with Wolverine's outbox
            opts.UseEntityFrameworkCoreTransactions();

            // Auto-apply transactions to handlers that use DbContext
            opts.Policies.AutoApplyTransactions();

            // ============================================================================
            // Modular Monolith: Separated Handler Mode
            // Each module's handlers execute independently in their own transaction scope
            // ============================================================================
            opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

            // ============================================================================
            // Durable Local Queues (Outbox Pattern for In-Process Messages)
            // ============================================================================
            opts.Policies.UseDurableLocalQueues();

            // Configure message identity for modular monolith
            // Allows same message to be delivered to multiple modules independently
            opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

            // ============================================================================
            // Global Error Handling with Retry Policies
            // ============================================================================
            opts.Policies.OnAnyException()
                .RetryWithCooldown(
                    TimeSpan.FromMilliseconds(50),
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromSeconds(1))
                .Then.MoveToErrorQueue();

            // ============================================================================
            // FluentValidation Middleware
            // ============================================================================
            opts.UseFluentValidation();

            // ============================================================================
            // Handler Discovery
            // ============================================================================
            foreach (var markerType in handlerAssemblyMarkerTypes)
            {
                opts.Discovery.IncludeAssembly(markerType.Assembly);
            }

            // ============================================================================
            // Development/Testing: Auto-provision message storage tables
            // ============================================================================
            opts.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.CreateOrUpdate;
        });
    }

    /// <summary>
    ///     Registers FluentValidation validators from the specified assemblies.
    /// </summary>
    public static IServiceCollection AddWolverineValidation(
        this IServiceCollection services,
        params Type[] assemblyMarkerTypes)
    {
        foreach (var markerType in assemblyMarkerTypes)
        {
            services.AddValidatorsFromAssemblyContaining(markerType, ServiceLifetime.Scoped);
        }

        return services;
    }
}
