using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using NetCommerce.Media.Application.Services;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Media.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage service for media storage.
/// Used when running with Aspire (Azurite locally, Azure Blob Storage in production).
/// </summary>
public sealed class AzureBlobStorageService : IStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly AzureBlobOptions _options;

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient,
        IOptions<AzureBlobOptions> options)
    {
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
    }

    public async Task<Result<string>> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

            var key = GenerateKey(folder, fileName);
            var blobClient = containerClient.GetBlobClient(key);

            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            };

            await blobClient.UploadAsync(
                fileStream,
                new BlobUploadOptions { HttpHeaders = blobHttpHeaders },
                cancellationToken);

            return Result.Success(key);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(
                Error.Failure("Storage.Upload.Failed", ex.Message));
        }
    }

    public async Task<Result<PresignedUploadUrl>> GetPresignedUploadUrlAsync(
        string fileName,
        string contentType,
        string folder,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

            var key = GenerateKey(folder, fileName);
            var blobClient = containerClient.GetBlobClient(key);

            // Create a SAS token for upload
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _options.ContainerName,
                BlobName = key,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiry)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);

            return Result.Success(new PresignedUploadUrl(
                sasUri.ToString(),
                key,
                DateTime.UtcNow.Add(expiry)));
        }
        catch (Exception ex)
        {
            return Result.Failure<PresignedUploadUrl>(
                Error.Failure("Storage.PresignedUrl.Failed", ex.Message));
        }
    }

    public async Task<Result<Stream>> DownloadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            var blobClient = containerClient.GetBlobClient(key);

            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return Result.Success(response.Value.Content);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return Result.Failure<Stream>(
                Error.NotFound("Storage.File.NotFound", $"File not found: {key}"));
        }
        catch (Exception ex)
        {
            return Result.Failure<Stream>(
                Error.Failure("Storage.Download.Failed", ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            var blobClient = containerClient.GetBlobClient(key);

            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                Error.Failure("Storage.Delete.Failed", ex.Message));
        }
    }

    public string GetPublicUrl(string key)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
        var blobClient = containerClient.GetBlobClient(key);
        return blobClient.Uri.ToString();
    }

    public Result<string> GetSignedUrl(string key, TimeSpan expiry)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            var blobClient = containerClient.GetBlobClient(key);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _options.ContainerName,
                BlobName = key,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiry)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return Result.Success(sasUri.ToString());
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(
                Error.Failure("Storage.SignedUrl.Failed", ex.Message));
        }
    }

    private static string GenerateKey(string folder, string fileName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var sanitizedFileName = SanitizeFileName(fileName);
        return $"{folder}/{timestamp}/{uniqueId}_{sanitizedFileName}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.ToLowerInvariant();
    }
}

/// <summary>
/// Configuration options for Azure Blob Storage.
/// </summary>
public class AzureBlobOptions
{
    public const string SectionName = "AzureBlob";

    /// <summary>
    /// The name of the blob container to use.
    /// </summary>
    public string ContainerName { get; set; } = "media";

    /// <summary>
    /// The base URL for generating public URLs.
    /// </summary>
    public string? BaseUrl { get; set; }
}
