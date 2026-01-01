using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

/// <summary>
///     Extension methods for registering the Outbox Processor.
/// </summary>
public static class OutboxServiceExtensions
{
    /// <summary>
    ///     Adds the Outbox Processor for the specified DbContext.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type that implements IOutboxDbContext.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section for OutboxProcessorOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutboxProcessor<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.Configure<OutboxProcessorOptions>(
            configuration.GetSection(OutboxProcessorOptions.SectionName));

        services.AddHostedService<OutboxProcessor<TDbContext>>();

        return services;
    }

    /// <summary>
    ///     Adds the Outbox Processor for the specified DbContext with custom options.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type that implements IOutboxDbContext.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure the options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutboxProcessor<TDbContext>(
        this IServiceCollection services,
        Action<OutboxProcessorOptions> configureOptions)
        where TDbContext : DbContext, IOutboxDbContext
    {
        services.Configure(configureOptions);
        services.AddHostedService<OutboxProcessor<TDbContext>>();

        return services;
    }
}