using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            services.AddScoped<IStorageService, S3StorageService>();
        }

        return services;
    }
}