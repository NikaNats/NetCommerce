using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Media.Infrastructure.Storage;

/// <summary>
///     Fail-fast validation for <see cref="S3Options"/> when the S3 storage path
///     is active. A missing endpoint or a localhost CDN URL must stop the process
///     at startup — not at 3 AM when the first product image upload 500s or every
///     storefront image renders broken. Checks apply to Production-like
///     environments (Production + Staging) only, matching StripeOptionsValidator.
///     Skipped entirely when the Azure Blob path is active (same detection rule
///     as MediaModule: a non-empty "blobs" connection string or an AzureBlob
///     section selects Azure).
/// </summary>
public sealed partial class S3OptionsValidator : IValidateOptions<S3Options>
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<S3OptionsValidator> _logger;

    public S3OptionsValidator(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<S3OptionsValidator> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, S3Options options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        if (IsAzurePathActive())
            return ValidateOptionsResult.Success;

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            Log.EndpointMissing(_logger, _environment.EnvironmentName);
            return ValidateOptionsResult.Fail(
                "Storage:Endpoint must be configured when the S3 storage path is active in Production/Staging.");
        }

        if (IsLoopback(options.Endpoint))
        {
            Log.LoopbackEndpoint(_logger, _environment.EnvironmentName, options.Endpoint);
            return ValidateOptionsResult.Fail(
                "Storage:Endpoint must not be localhost in Production/Staging. Point it at MinIO/AWS and set Storage:CdnBaseUrl to match.");
        }

        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            Log.BucketMissing(_logger, _environment.EnvironmentName);
            return ValidateOptionsResult.Fail(
                "Storage:BucketName must be configured when the S3 storage path is active in Production/Staging.");
        }

        if (string.IsNullOrWhiteSpace(options.CdnBaseUrl))
        {
            Log.CdnBaseUrlMissing(_logger, _environment.EnvironmentName);
            return ValidateOptionsResult.Fail(
                "Storage:CdnBaseUrl must be configured when the S3 storage path is active in Production/Staging — product image URLs are built from it.");
        }

        // IAM-role deployments (ECR/EKS instance profiles) legitimately carry no
        // static keys; the AWS SDK resolves credentials from the environment.
        // Outside AWS endpoints, missing keys are always a misconfiguration.
        var isAws = options.Endpoint.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase);
        if (!isAws && (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey)))
        {
            Log.KeysMissing(_logger, _environment.EnvironmentName);
            return ValidateOptionsResult.Fail(
                "Storage:AccessKey and Storage:SecretKey must be configured for non-AWS S3 endpoints in Production/Staging.");
        }

        return ValidateOptionsResult.Success;
    }

    private bool IsAzurePathActive()
    {
        return !string.IsNullOrWhiteSpace(_configuration.GetConnectionString("blobs"))
               || _configuration.GetSection("AzureBlob").Exists();
    }

    private static bool IsLoopback(string endpoint)
    {
        return endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase)
               || endpoint.Contains("127.0.0.1", StringComparison.Ordinal)
               || endpoint.Contains("::1", StringComparison.Ordinal);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Critical,
            Message = "FATAL: Storage:Endpoint is missing in {Environment} while the S3 storage path is active. Set it (or provide ConnectionStrings__blobs for Azure).")]
        public static partial void EndpointMissing(ILogger logger, string environment);

        [LoggerMessage(EventId = 2, Level = LogLevel.Critical,
            Message = "FATAL: Storage:Endpoint is a loopback address ({Endpoint}) in {Environment}. Product images would resolve to localhost.")]
        public static partial void LoopbackEndpoint(ILogger logger, string environment, string endpoint);

        [LoggerMessage(EventId = 3, Level = LogLevel.Critical,
            Message = "FATAL: Storage:BucketName is missing in {Environment} while the S3 storage path is active.")]
        public static partial void BucketMissing(ILogger logger, string environment);

        [LoggerMessage(EventId = 4, Level = LogLevel.Critical,
            Message = "FATAL: Storage:CdnBaseUrl is missing in {Environment} while the S3 storage path is active. Every product image URL is built from it.")]
        public static partial void CdnBaseUrlMissing(ILogger logger, string environment);

        [LoggerMessage(EventId = 5, Level = LogLevel.Critical,
            Message = "FATAL: Storage:AccessKey/SecretKey are missing in {Environment} for a non-AWS S3 endpoint.")]
        public static partial void KeysMissing(ILogger logger, string environment);
    }
}
