using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthPath = "/health/ready";
    private const string AlivePath = "/health/alive";
    private const string LiveTag = "live";
    private const string HealthStatusTag = "aspire.health.status";

    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        public TBuilder AddServiceDefaults()
        {
            builder.ConfigureOpenTelemetry();
            builder.AddDefaultHealthChecks();
            builder.Services.AddServiceDiscovery();

            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });

            return builder;
        }

        public TBuilder ConfigureOpenTelemetry()
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            var otelBuilder = builder.Services.AddOpenTelemetry();

            otelBuilder.WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                // Custom application meters for business process observability
                metrics.AddMeter("NetCommerce.Ordering"); // OrderFulfillmentSaga state gauges
                metrics.AddMeter("Wolverine");            // Outbox queue depth, retry counts
            });

            // 4. Configure Tracing
            otelBuilder.WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName);

                tracing.AddAspNetCoreInstrumentation(options =>
                    {
                        options.EnrichWithHttpRequest = (activity, request) => activity.SetTag("http.request.length", request.ContentLength);
                        options.EnrichWithHttpResponse = (activity, response) => activity.SetTag("http.response.length", response.ContentLength);

                        options.Filter = _ => true;
                    })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddRedisInstrumentation();
            });

            var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
            if (useOtlpExporter)
            {
                otelBuilder.UseOtlpExporter();
            }

            return builder;
        }

        public TBuilder AddDefaultHealthChecks()
        {
            builder.Services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);
            return builder;
        }
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

        if (Activity.Current is { } activity)
        {
            activity.SetTag(HealthStatusTag, result.Status.ToString());
            if (result.Status != HealthStatus.Healthy)
            {
                activity.SetStatus(ActivityStatusCode.Error, $"Health check failed: {result.Status}");
                foreach (var entry in result.Entries.Where(e => e.Value.Status != HealthStatus.Healthy))
                {
                    activity.SetTag($"health.failure.{entry.Key}", entry.Value.Exception?.Message ?? entry.Value.Description);
                }
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

// -------------------------------------------------------------------------
// SOURCE GENERATION CONTEXT
// -------------------------------------------------------------------------

public record HealthResponse(
    string Status,
    double TotalDuration,
    HealthEntry[] Entries);

public record HealthEntry(
    string Name,
    string Status,
    double Duration,
    string? Description,
    string? Exception,
    IEnumerable<string> Tags);

[JsonSerializable(typeof(HealthResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
public partial class HealthCheckJsonContext : JsonSerializerContext
{
}
