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

        // Redis (Aspire will inject the connection string).
        // Registered only when configured: the Aspire client adds its own
        // health check, and an unconfigured client turns /health/ready red for
        // a dependency nothing in this mode needs. Production always sets it
        // (Data Protection fails fast at boot without it).
        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis")))
        {
            builder.AddRedisClient("redis");
        }

        // Azure Blob Storage (Aspire will inject the connection string).
        // Same guard, same reason — plus driver selection: an empty "blobs"
        // string selects the S3 path in MediaModule, and a registered-but-
        // unconfigured Azure client fails the readiness probe in S3 mode.
        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("blobs")))
        {
            builder.AddAzureBlobServiceClient("blobs");
        }

        // Seq for structured logging (Aspire will configure OTLP endpoint)
        // Make Seq optional - if ServerUrl is not configured (e.g., in tests), skip it
        var seqServerUrl = builder.Configuration["Seq:ServerUrl"];
        if (!string.IsNullOrEmpty(seqServerUrl))
        {
            builder.AddSeqEndpoint("seq");
        }

        // Meilisearch for product search (read model).
        // Same guard: without a connection string there is no index to probe,
        // and the Aspire health check would fail readiness for it.
        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("meilisearch")))
        {
            builder.AddMeilisearchClient("meilisearch");
        }

        return builder;
    }
}
