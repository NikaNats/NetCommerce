#nullable enable
using System.Net;

namespace NetCommerce.Api.Extensions;

/// <summary>
///     Resilient rate-limit partition key extractor.
///     Handles ForwardedHeaders, IPv6-mapped IPv4 normalization, and loopback fallbacks.
/// </summary>
public static class RateLimitExtensions
{
    public static string GetRateLimitPartitionKey(this HttpContext context)
    {
        // 1. Prefer authenticated User ID (immune to IP spoofing)
        var userId = context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId)) return $"user:{userId}";

        var nameId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(nameId)) return $"user:{nameId}";

        // 2. Fallback to RemoteIpAddress (populated correctly by ForwardedHeaders middleware)
        var ip = context.Connection.RemoteIpAddress;
        if (ip == null || IPAddress.IsLoopback(ip))
        {
            return "unknown-proxy";
        }

        var normalizedIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
        return $"ip:{normalizedIp}";
    }
}
