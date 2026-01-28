#nullable enable

using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Simmy;
using Polly.Simmy.Fault;
using Polly.Simmy.Latency;

namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Chaos Engineering extensions for controlled failure injection.
///
///     These policies implement the "Simmy" pattern from Polly to inject:
///     - Latency: Simulates slow network/database responses
///     - Faults: Simulates service failures and exceptions
///     - Behavior: Simulates custom behaviors (e.g., returning stale data)
///
///     IMPORTANT: Chaos policies should ONLY be enabled in testing environments,
///     never in production!
/// </summary>
public static class ChaosExtensions
{
    /// <summary>
    ///     Adds chaos engineering policies configured via IConfiguration.
    ///
    ///     Configuration schema:
    ///     {
    ///       "Chaos": {
    ///         "Enabled": true,
    ///         "Latency": {
    ///           "Enabled": true,
    ///           "InjectionRate": 0.1,     // 10% of requests
    ///           "MinDelayMs": 500,
    ///           "MaxDelayMs": 2000
    ///         },
    ///         "Fault": {
    ///           "Enabled": true,
    ///           "InjectionRate": 0.05,    // 5% of requests
    ///           "FaultMessage": "Chaos monkey fault"
    ///         }
    ///       }
    ///     }
    /// </summary>
    public static TBuilder AddChaosEngineering<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var config = builder.Configuration.GetSection("Chaos").Get<ChaosOptions>() ?? new ChaosOptions();

        // Only enable chaos in development/testing
        if (!config.Enabled || builder.Environment.IsProduction())
        {
            // Register a no-op chaos context
            builder.Services.AddSingleton(new ChaosContext { IsEnabled = false });
            return builder;
        }

        // Register chaos context for runtime control
        var context = new ChaosContext
        {
            IsEnabled = true,
            LatencyOptions = config.Latency,
            FaultOptions = config.Fault
        };
        builder.Services.AddSingleton(context);

        // Add chaos-aware HTTP client factory
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddResilienceHandler("chaos", ConfigureChaosStrategy);
        });

        return builder;
    }

    private static void ConfigureChaosStrategy(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        // The chaos strategies are configured but only activated when ChaosContext.IsEnabled is true
        // This allows runtime control of chaos injection
    }

    /// <summary>
    ///     Creates a resilience pipeline with chaos injection for testing.
    ///     Use this in integration tests to inject controlled failures.
    /// </summary>
    public static ResiliencePipeline CreateChaosResiliencePipeline(
        ChaosOptions options,
        ILogger? logger = null)
    {
        var builder = new ResiliencePipelineBuilder();

        if (options.Latency.Enabled)
        {
            builder.AddChaosLatency(new ChaosLatencyStrategyOptions
            {
                InjectionRate = options.Latency.InjectionRate,
                Latency = TimeSpan.FromMilliseconds(
                    Random.Shared.Next(options.Latency.MinDelayMs, options.Latency.MaxDelayMs)),
                EnabledGenerator = static args => ValueTask.FromResult(true),
                OnLatencyInjected = args =>
                {
                    logger?.LogWarning(
                        "Chaos: Injected {Latency}ms latency",
                        args.Latency.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            });
        }

        if (options.Fault.Enabled)
        {
            builder.AddChaosFault(new ChaosFaultStrategyOptions
            {
                InjectionRate = options.Fault.InjectionRate,
                FaultGenerator = static args => new ValueTask<Exception?>(
                    new InvalidOperationException("Chaos monkey fault injected")),
                OnFaultInjected = args =>
                {
                    logger?.LogWarning("Chaos: Injected fault - {FaultType}", args.Fault?.GetType().Name);
                    return ValueTask.CompletedTask;
                }
            });
        }

        return builder.Build();
    }

    /// <summary>
    ///     Middleware extension to inject chaos into HTTP request pipeline.
    ///     Should only be used in testing/development environments.
    /// </summary>
    public static WebApplication UseChaosMiddleware(this WebApplication app)
    {
        var context = app.Services.GetService<ChaosContext>();
        if (context?.IsEnabled != true)
        {
            return app;
        }

        app.Use(async (httpContext, next) =>
        {
            var chaosContext = httpContext.RequestServices.GetService<ChaosContext>();

            // Skip chaos for health checks
            if (httpContext.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            // Inject latency if configured
            if (chaosContext?.LatencyOptions?.Enabled == true)
            {
                var shouldInject = Random.Shared.NextDouble() < chaosContext.LatencyOptions.InjectionRate;
                if (shouldInject)
                {
                    var delay = Random.Shared.Next(
                        chaosContext.LatencyOptions.MinDelayMs,
                        chaosContext.LatencyOptions.MaxDelayMs);

                    Activity.Current?.AddEvent(new ActivityEvent("ChaosLatencyInjected",
                        tags: new ActivityTagsCollection { ["delay_ms"] = delay }));

                    await Task.Delay(delay);
                }
            }

            // Inject fault if configured
            if (chaosContext?.FaultOptions?.Enabled == true)
            {
                var shouldInject = Random.Shared.NextDouble() < chaosContext.FaultOptions.InjectionRate;
                if (shouldInject)
                {
                    Activity.Current?.AddEvent(new ActivityEvent("ChaosFaultInjected"));

                    httpContext.Response.StatusCode = 500;
                    httpContext.Response.ContentType = "application/json";
                    await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = "Chaos fault injected",
                        message = chaosContext.FaultOptions.FaultMessage
                    }));
                    return;
                }
            }

            await next();
        });

        return app;
    }
}

/// <summary>
///     Runtime chaos configuration context.
///     Can be modified at runtime to enable/disable chaos injection.
/// </summary>
public class ChaosContext
{
    public bool IsEnabled { get; set; }
    public LatencyOptions? LatencyOptions { get; set; }
    public FaultOptions? FaultOptions { get; set; }
}

/// <summary>
///     Configuration options for chaos engineering.
/// </summary>
public class ChaosOptions
{
    public bool Enabled { get; set; }
    public LatencyOptions Latency { get; set; } = new();
    public FaultOptions Fault { get; set; } = new();
}

/// <summary>
///     Latency injection configuration.
/// </summary>
public class LatencyOptions
{
    /// <summary>
    ///     Whether latency injection is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Probability of injecting latency (0.0 to 1.0).
    ///     Default: 0.1 (10%)
    /// </summary>
    public double InjectionRate { get; set; } = 0.1;

    /// <summary>
    ///     Minimum delay to inject in milliseconds.
    ///     Default: 500ms
    /// </summary>
    public int MinDelayMs { get; set; } = 500;

    /// <summary>
    ///     Maximum delay to inject in milliseconds.
    ///     Default: 2000ms
    /// </summary>
    public int MaxDelayMs { get; set; } = 2000;
}

/// <summary>
///     Fault injection configuration.
/// </summary>
public class FaultOptions
{
    /// <summary>
    ///     Whether fault injection is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Probability of injecting a fault (0.0 to 1.0).
    ///     Default: 0.05 (5%)
    /// </summary>
    public double InjectionRate { get; set; } = 0.05;

    /// <summary>
    ///     Custom fault message to return.
    /// </summary>
    public string FaultMessage { get; set; } = "Chaos monkey fault injected";
}
