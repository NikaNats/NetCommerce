#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Media.Application.Services;

namespace NetCommerce.Media.Infrastructure.Storage;

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
            // Defensive Length check for non-seekable streams
            var streamLength = fileStream.CanSeek ? fileStream.Length : ImageInspector.MaxSizeBytes - 1;

            // 1. Magic-Byte Validation & MIME Detection
            var validationResult = await ImageInspector.ValidateAsync(fileStream, streamLength, cancellationToken);
            if (!validationResult.IsSuccess)
                return Result.Failure<string>(validationResult.Error);

            var validatedImage = validationResult.Value;

            // 2. Resolve Container Client (Direct access without redundant CreateIfNotExists call on every upload)
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            var key = GenerateSafeKey(folder, validatedImage.SafeFileName);
            var blobClient = containerClient.GetBlobClient(key);

            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = validatedImage.MimeType // True detected MIME type
            };

            // 3. Upload Stream
            await blobClient.UploadAsync(
                fileStream,
                new BlobUploadOptions { HttpHeaders = blobHttpHeaders },
                cancellationToken);

            return Result.Success(key);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(Error.Failure("Storage.Upload.Failed", ex.Message));
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
            var sanitizedName = SanitizeFileName(fileName);
            var key = GenerateSafeKey(folder, $"{Guid.NewGuid():N}_{sanitizedName}");
            var blobClient = containerClient.GetBlobClient(key);

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
            return Result.Failure<PresignedUploadUrl>(Error.Failure("Storage.PresignedUrl.Failed", ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
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
            return Result.Failure(Error.Failure("Storage.Delete.Failed", ex.Message));
        }
    }

    public string GetPublicUrl(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;

        if (!string.IsNullOrEmpty(_options.BaseUrl))
            return $"{_options.BaseUrl.TrimEnd('/')}/{key.TrimStart('/')}";

        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
        return containerClient.GetBlobClient(key).Uri.ToString();
    }

    public async Task<Result<Stream>> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            var blobClient = containerClient.GetBlobClient(key);
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return Result.Success(response.Value.Content);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return Result.Failure<Stream>(Error.NotFound("Storage.File.NotFound", $"File not found: {key}"));
        }
        catch (Exception ex)
        {
            return Result.Failure<Stream>(Error.Failure("Storage.Download.Failed", ex.Message));
        }
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
            return Result.Failure<string>(Error.Failure("Storage.SignedUrl.Failed", ex.Message));
        }
    }

    private static string GenerateSafeKey(string folder, string safeFileName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var sanitizedFolder = folder.Trim('/').Replace('\\', '/');
        return $"{sanitizedFolder}/{timestamp}/{safeFileName}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var nameOnly = Path.GetFileName(fileName);
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", nameOnly.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.ToLowerInvariant();
    }
}

public class AzureBlobOptions
{
    public const string SectionName = "AzureBlob";
    public string ContainerName { get; set; } = "media";
    public string? BaseUrl { get; set; }
}
