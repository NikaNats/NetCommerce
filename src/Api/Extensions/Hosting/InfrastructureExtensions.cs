using Microsoft.Extensions.Caching.Hybrid;

namespace NetCommerce.Api.Extensions.Hosting;

/// <summary>
///     Aspire-managed infrastructure clients: HybridCache, Redis, Azure Blob, Seq, Meilisearch.
///     Extracted verbatim from Program.cs — option values and the optional-Seq guard are unchanged.
/// </summary>
public static class InfrastructureExtensions
{
    public static IHostApplicationBuilder AddEnterpriseStorage(this IHostApplicationBuilder builder)
    {
#pragma warning disable EXTEXP0018 // HybridCache is still evolving in .NET 10
        // 1. Add HybridCache to the DI container
        builder.Services.AddHybridCache(options =>
        {
            // 2025 Best Practice: Set global defaults
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(60),
                LocalCacheExpiration = TimeSpan.FromMinutes(5) // L1 (RAM) is shorter for consistency
            };
        });
#pragma warning restore EXTEXP0018

        // Redis (Aspire will inject the connection string)
        builder.AddRedisClient("redis");

        // Azure Blob Storage (Aspire will inject the connection string)
        builder.AddAzureBlobServiceClient("blobs");

        // Seq for structured logging (Aspire will configure OTLP endpoint)
        // Make Seq optional - if ServerUrl is not configured (e.g., in tests), skip it
        var seqServerUrl = builder.Configuration["Seq:ServerUrl"];
        if (!string.IsNullOrEmpty(seqServerUrl))
        {
            builder.AddSeqEndpoint("seq");
        }

        // Meilisearch for product search (read model)
        builder.AddMeilisearchClient("meilisearch");

        return builder;
    }
}
