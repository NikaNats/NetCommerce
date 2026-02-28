#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace NetCommerce.Kernel.Wolverine.DeadLetters;

/// <summary>
///     Extension methods for registering the Dead Letter Queue monitor.
/// </summary>
public static class DeadLetterQueueExtensions
{
    /// <summary>
    ///     Adds the Wolverine Dead Letter Queue monitor as a hosted service.
    ///
    ///     <para>
    ///     <b>Configuration (appsettings.json):</b>
    ///     <code>
    ///     {
    ///       "Wolverine": {
    ///         "DeadLetterMonitor": {
    ///           "ConnectionString": "Host=...",
    ///           "CheckIntervalSeconds": 60,
    ///           "AlertThreshold": 10,
    ///           "EnableHealthCheck": true
    ///         }
    ///       }
    ///     }
    ///     </code>
    ///     </para>
    ///
    ///     <para>
    ///     <b>Metrics exposed:</b>
    ///     - wolverine_dlq_new_messages_total
    ///     - wolverine_dlq_message_age_seconds
    ///     </para>
    /// </summary>
    public static IServiceCollection AddDeadLetterQueueMonitor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.AddOptions<DeadLetterQueueMonitorOptions>()
            .Bind(configuration.GetSection(DeadLetterQueueMonitorOptions.SectionName))
            .PostConfigure(options =>
            {
                // Default to primary connection string if not explicitly configured
                options.ConnectionString ??= configuration.GetConnectionString("DefaultConnection")
                    ?? configuration.GetConnectionString("OrderingDb");
            })
            .ValidateOnStart();

        // Register the monitor as a hosted service
        services.AddSingleton<DeadLetterQueueMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<DeadLetterQueueMonitor>());

        // Register the admin repository (scoped, uses deferred options resolution)
        services.AddScoped<DeadLetterEnvelopeRepository>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DeadLetterQueueMonitorOptions>>().Value;
            var cs = opts.ConnectionString
                ?? throw new InvalidOperationException(
                    "DeadLetterQueueMonitorOptions.ConnectionString is required for DeadLetterEnvelopeRepository");
            return new DeadLetterEnvelopeRepository(cs);
        });

        // Register health check if enabled
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "wolverine-dlq",
                sp => sp.GetRequiredService<DeadLetterQueueMonitor>(),
                HealthStatus.Degraded,
                ["wolverine", "messaging", "dlq"],
                TimeSpan.FromSeconds(30)));

        return services;
    }

    /// <summary>
    ///     Adds the DLQ monitor with explicit options configuration.
    /// </summary>
    public static IServiceCollection AddDeadLetterQueueMonitor(
        this IServiceCollection services,
        Action<DeadLetterQueueMonitorOptions> configure)
    {
        services.AddOptions<DeadLetterQueueMonitorOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.AddSingleton<DeadLetterQueueMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<DeadLetterQueueMonitor>());

        // Register the admin repository (scoped, uses deferred options resolution)
        services.AddScoped<DeadLetterEnvelopeRepository>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DeadLetterQueueMonitorOptions>>().Value;
            var cs = opts.ConnectionString
                ?? throw new InvalidOperationException(
                    "DeadLetterQueueMonitorOptions.ConnectionString is required for DeadLetterEnvelopeRepository");
            return new DeadLetterEnvelopeRepository(cs);
        });

        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "wolverine-dlq",
                sp => sp.GetRequiredService<DeadLetterQueueMonitor>(),
                HealthStatus.Degraded,
                ["wolverine", "messaging", "dlq"],
                TimeSpan.FromSeconds(30)));

        return services;
    }
}
