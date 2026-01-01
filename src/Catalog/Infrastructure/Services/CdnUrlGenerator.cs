using Microsoft.Extensions.Options;
using NetCommerce.Catalog.Application.Products.Queries;

namespace NetCommerce.Catalog.Infrastructure.Services;

/// <summary>
///     CDN URL generator for product images stored in S3/MinIO.
/// </summary>
public sealed class CdnUrlGenerator : ICdnUrlGenerator
{
    private readonly string _cdnBaseUrl;

    public CdnUrlGenerator(IOptions<StorageOptions> options)
    {
        _cdnBaseUrl = options.Value.CdnBaseUrl.TrimEnd('/');
    }

    public string GenerateUrl(string imageKey)
    {
        if (string.IsNullOrWhiteSpace(imageKey)) return string.Empty;

        return $"{_cdnBaseUrl}/{imageKey.TrimStart('/')}";
    }
}

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string CdnBaseUrl { get; set; } = string.Empty;
}