using Amazon;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetCommerce.Media.Application.Services;
using NetCommerce.Media.Infrastructure.Storage;

namespace NetCommerce.Media.Infrastructure;

public static class MediaModule
{
    public static IServiceCollection AddMediaModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Check if we're using Azure Blob Storage (Aspire) or S3 (MinIO/AWS)
        var useAzureBlob = configuration.GetConnectionString("blobs") != null
                           || configuration.GetSection("AzureBlob").Exists();

        if (useAzureBlob)
        {
            // Azure Blob Storage (used with Aspire)
            services.Configure<AzureBlobOptions>(configuration.GetSection(AzureBlobOptions.SectionName));

            // BlobServiceClient will be injected by Aspire via AddAzureBlobClient
            services.AddScoped<IStorageService, AzureBlobStorageService>();
        }
        else
        {
            // S3 Configuration (MinIO/AWS fallback)
            services.Configure<S3Options>(configuration.GetSection(S3Options.SectionName));

            // The AWS SDK client must be explicitly registered; S3StorageService cannot activate
            // without it. Construction is offline-safe: credentials resolve lazily from
            // config/env/instance profile on first request.
            services.AddSingleton<IAmazonS3>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<S3Options>>().Value;
                var config = new AmazonS3Config
                {
                    ServiceURL = string.IsNullOrEmpty(options.Endpoint) ? null : options.Endpoint,
                    ForcePathStyle = options.ForcePathStyle
                };

                if (!string.IsNullOrEmpty(options.AccessKey) && !string.IsNullOrEmpty(options.SecretKey))
                {
                    return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
                }

                return new AmazonS3Client(config);
            });

            services.AddScoped<IStorageService, S3StorageService>();
        }

        return services;
    }
}