using NetCommerce.Api.Serialization;
using NetCommerce.Kernel.Core.Serialization;

namespace NetCommerce.Api.Extensions.Hosting;

/// <summary>
///     JSON source generation for Native AOT. Extracted verbatim from Program.cs:
///     strict source generation with no reflection fallback — a type missing from
///     ApiJsonContext fails fast in Dev instead of silently breaking trimmed binaries.
/// </summary>
public static class SerializationExtensions
{
    public static IServiceCollection AddAotJsonSerialization(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            // CRITICAL: Enforce strict Source Generation - no reflection fallback.
            // This ensures all types are pre-compiled for Native AOT.
            // If a type is missing from ApiJsonContext, it will fail fast in Dev.
            options.SerializerOptions.TypeInfoResolverChain.Clear();
            options.SerializerOptions.TypeInfoResolverChain.Add(ApiJsonContext.Default);

            // Custom converters for Value Objects
            options.SerializerOptions.Converters.Add(new StronglyTypedIdJsonConverterFactory());
        });

        return services;
    }
}
