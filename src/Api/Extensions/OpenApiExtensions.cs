#nullable enable
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace NetCommerce.Api.Extensions;

public static class OpenApiExtensions
{
    public static IHostApplicationBuilder AddNetCommerceOpenApi(this IHostApplicationBuilder builder)
    {
        // .NET 10 Native OpenAPI replacement for SwaggerGen
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = "NetCommerce Enterprise API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
        });

        return builder;
    }

    public static WebApplication UseNetCommerceOpenApi(this WebApplication app)
    {
        // Serves the document at /openapi/v1.json
        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            // Scalar is the 2025 standard for .NET 10 compatibility
            app.MapScalarApiReference(options =>
            {
                options.WithTitle("NetCommerce API Explorer")
                       .WithTheme(ScalarTheme.Moon)
                       .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
            }).WithSecurityHeadersPolicy("ScalarDevUI");
        }

        return app;
    }
}
