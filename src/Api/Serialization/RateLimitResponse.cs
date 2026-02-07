using System.Text.Json.Serialization;

namespace NetCommerce.Api.Serialization;

/// <summary>
///     Rate limit exceeded response body for AOT-safe serialization.
/// </summary>
internal sealed class RateLimitResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = default!;

    [JsonPropertyName("message")]
    public string Message { get; init; } = default!;

    [JsonPropertyName("retryAfter")]
    public double RetryAfter { get; init; }
}
