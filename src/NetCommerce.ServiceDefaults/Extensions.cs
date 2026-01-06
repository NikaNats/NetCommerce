using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthPath = "/health/ready";
    private const string AlivePath = "/health/alive";
    private const string LiveTag = "live";
    private const string HealthStatusTag = "aspire.health.status";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var serviceName = builder.Environment.ApplicationName;

        // 1. Register a singleton to hold ActivitySource and Meter.
        // Documentation recommends a custom type to avoid type collisions and frequent recreation.
        builder.Services.AddSingleton(new ServiceInstrumentation(serviceName));

        // 2. Configure Unified OpenTelemetry SDK
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                    ["host.name"] = Environment.MachineName
                }))
            .WithLogging(logging =>
            {
                logging.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation() // Requires OpenTelemetry.Instrumentation.Process
                    .AddMeter(serviceName)
                    .AddMeter("Wolverine")
                    // Best Practice: Enable Exemplars for Metric-to-Trace correlation
                    .SetExemplarFilter(ExemplarFilterType.TraceBased)
                    .AddView("http.server.request.duration",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries = [0, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 10]
                        })
                    .AddOtlpExporter();
            })
            .WithTracing(tracing =>
            {
                tracing.SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
                    .AddSource(serviceName)
                    .AddSource("Wolverine")
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = (httpContext) =>
                            !httpContext.Request.Path.StartsWithSegments("/health") &&
                            !httpContext.Request.Path.StartsWithSegments("/swagger");

                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            // Best Practice: Check IsAllDataRequested before performing expensive tag operations
                            if (activity.IsAllDataRequested)
                            {
                                var userAgent = request.Headers.UserAgent.ToString();
                                if (!string.IsNullOrEmpty(userAgent))
                                {
                                    activity.SetTag("http.user_agent", userAgent);
                                }
                            }
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
                    .AddOtlpExporter();
            });

        // 3. Discovery and Resilience
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthPath, new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteHealthResponse
        });

        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(LiveTag),
            ResponseWriter = WriteHealthResponse
        });

        return app;
    }

    private static async Task WriteHealthResponse(HttpContext context, HealthReport result)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        // Access the current activity from the Tracing API
        if (Activity.Current is { } activity)
        {
            activity.SetTag(HealthStatusTag, result.Status.ToString());
            if (result.Status != HealthStatus.Healthy)
            {
                activity.SetStatus(ActivityStatusCode.Error, "Health check failed");
            }
        }

        var response = new HealthResponse(
            result.Status.ToString(),
            result.TotalDuration.TotalMilliseconds,
            result.Entries.Select(e => new HealthEntry(
                e.Key,
                e.Value.Status.ToString(),
                e.Value.Duration.TotalMilliseconds,
                e.Value.Description,
                e.Value.Exception?.Message,
                e.Value.Tags
            )).ToArray()
        );

        await context.Response.WriteAsJsonAsync(response, HealthCheckJsonContext.Default.HealthResponse);
    }
}

/// <summary>
/// Custom type to hold references for ActivitySource and Meter.
/// Prevents frequent allocations and ensures consistent naming.
/// </summary>
public sealed class ServiceInstrumentation : IDisposable
{
    public ActivitySource ActivitySource { get; }
    public Meter Meter { get; }

    public ServiceInstrumentation(string name, string version = "1.0.0")
    {
        ActivitySource = new ActivitySource(name, version);
        Meter = new Meter(name, version);
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}

public record HealthResponse(string Status, double TotalDuration, HealthEntry[] Entries);
public record HealthEntry(string Name, string Status, double Duration, string? Description, string? Exception, IEnumerable<string> Tags);

[JsonSerializable(typeof(HealthResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
public partial class HealthCheckJsonContext : JsonSerializerContext { }
