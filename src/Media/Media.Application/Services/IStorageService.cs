using NetCommerce.SharedKernel.Results;

namespace NetCommerce.Media.Application.Services;

/// <summary>
///     Storage service interface for S3-compatible object storage.
/// </summary>
public interface IStorageService
{
    /// <summary>
    ///     Uploads a file and returns the storage key.
    /// </summary>
    Task<Result<string>> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a pre-signed URL for upload (for direct browser uploads).
    /// </summary>
    Task<Result<PresignedUploadUrl>> GetPresignedUploadUrlAsync(
        string fileName,
        string contentType,
        string folder,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes a file by key.
    /// </summary>
    Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the public CDN URL for a key.
    /// </summary>
    string GetPublicUrl(string key);
}

public record PresignedUploadUrl(
    string Url,
    string Key,
    DateTime ExpiresAt,
    Dictionary<string, string>? FormFields = null);