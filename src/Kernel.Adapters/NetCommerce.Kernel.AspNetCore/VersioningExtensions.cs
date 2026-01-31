#nullable enable
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Asp.Versioning.Builder; // Required for ApiVersionSet
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace NetCommerce.Kernel.AspNetCore;

/// <summary>
///     API versioning extensions for NetCommerce APIs.
/// </summary>
public static class VersioningExtensions
{
    /// <summary>
    ///     Adds API versioning to the service collection with URL segment, header, and query string readers.
    /// </summary>
    public static IServiceCollection AddVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
            {
                // 1. Set the baseline
                options.DefaultApiVersion = new ApiVersion(1, 0);

                // 2. DON'T break legacy clients if they forget the version
                options.AssumeDefaultVersionWhenUnspecified = true;

                // 3. IMPORTANT: Tell the client which versions are available via Headers
                // (Sends 'api-supported-versions: 1.0, 2.0')
                options.ReportApiVersions = true;

                // 4. Use multiple readers for flexibility: path, header, and query string
                options.ApiVersionReader = ApiVersionReader.Combine(
                    new UrlSegmentApiVersionReader(),
                    new HeaderApiVersionReader("x-api-version"),
                    new QueryStringApiVersionReader("api-version")
                );
            })
            .AddApiExplorer(options =>
            {
                // Group APIs by version (e.g., "v1", "v2")
                options.GroupNameFormat = "'v'V";
                // Substitute the version in URLs for Swagger
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }

    /// <summary>
    ///     Gets the default API version set for NetCommerce APIs (v1.0).
    /// </summary>
    public static ApiVersionSet GetDefaultApiVersionSet(this WebApplication app)
    {
        return app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();
    }
}
