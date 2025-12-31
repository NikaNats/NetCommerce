using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using NetCommerce.Media.Application.Services;
using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Media.Infrastructure.Storage;

/// <summary>
/// S3-compatible storage service (works with AWS S3 and MinIO).
/// </summary>
public sealed class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3Options _options;

    public S3StorageService(IAmazonS3 s3Client, IOptions<S3Options> options)
    {
        _s3Client = s3Client;
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
            var key = GenerateKey(folder, fileName);
            
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request, cancellationToken);

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
            var key = GenerateKey(folder, fileName);
            
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(expiry),
                ContentType = contentType
            };

            var url = await _s3Client.GetPreSignedURLAsync(request);

            return Result.Success(new PresignedUploadUrl(
                url,
                key,
                DateTime.UtcNow.Add(expiry)));
        }
        catch (Exception ex)
        {
            return Result.Failure<PresignedUploadUrl>(
                Error.Failure("Storage.Presign.Failed", ex.Message));
        }
    }

    public async Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(request, cancellationToken);

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
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        return $"{_options.CdnBaseUrl.TrimEnd('/')}/{key.TrimStart('/')}";
    }

    private static string GenerateKey(string folder, string fileName)
    {
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var sanitizedFileName = SanitizeFileName(fileName);
        return $"{folder.Trim('/')}/{uniqueId}-{sanitizedFileName}";
    }

    private static string SanitizeFileName(string fileName)
    {
        return fileName
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "")
            .ToLowerInvariant();
    }
}

public class S3Options
{
    public const string SectionName = "Storage";
    
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string CdnBaseUrl { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true; // Required for MinIO
}
