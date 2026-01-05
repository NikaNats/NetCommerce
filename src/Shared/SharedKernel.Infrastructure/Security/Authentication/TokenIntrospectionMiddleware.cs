#region

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

/// <summary>
///     Zero-Trust Token Introspection Middleware (The "Kill Switch").
///     Purpose: Immediately revoke access when a user is banned/disabled in Keycloak,
///     rather than waiting for JWT expiration (typically 15 minutes).
///     Implements RFC 7662 (OAuth 2.0 Token Introspection) to validate tokens
///     against Keycloak's introspection endpoint on every request.
///     Performance Optimization:
///     - Caches introspection results in Redis for configurable duration
///     - Uses token hash as cache key (never stores actual token)
///     - Short-circuits if introspection is disabled
/// </summary>
public sealed class TokenIntrospectionMiddleware
{
    private readonly ILogger<TokenIntrospectionMiddleware> _logger;
    private readonly RequestDelegate _next;

    public TokenIntrospectionMiddleware(
        RequestDelegate next,
        ILogger<TokenIntrospectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IHttpClientFactory clientFactory,
        IOptions<ZeroTrustAuthOptions> options,
        IDistributedCache? cache = null)
    {
        ZeroTrustAuthOptions authOptions = options.Value;

        // Short-circuit if introspection is disabled
        if (!authOptions.IntrospectionEnabled)
        {
            await _next(context);
            return;
        }

        // Skip if no token present (let standard AuthN handle 401 later)
        string? token = await context.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token))
        {
            await _next(context);
            return;
        }

        // Check cache first (performance optimization)
        string cacheKey = $"introspection:{ComputeTokenHash(token)}";
        if (cache is not null)
        {
            string? cachedResult = await cache.GetStringAsync(cacheKey);
            if (cachedResult is not null)
            {
                if (cachedResult == "active")
                {
                    await _next(context);
                    return;
                }

                // Token was revoked
                _logger.LogWarning("Cached introspection result: Token revoked for user {User}",
                    context.User.Identity?.Name ?? "unknown");
                await RejectRequest(context, "Token has been revoked");
                return;
            }
        }

        // Perform introspection against Keycloak
        IntrospectionResult introspectionResult = await IntrospectTokenAsync(token, authOptions, clientFactory);

        // Cache the result
        if (cache is not null)
        {
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(authOptions.IntrospectionCacheSeconds)
            };
            await cache.SetStringAsync(cacheKey, introspectionResult.IsActive ? "active" : "revoked", cacheOptions);
        }

        if (!introspectionResult.IsActive)
        {
            _logger.LogWarning(
                "Token introspection failed: Token revoked for user {User}. Reason: {Reason}",
                context.User.Identity?.Name ?? "unknown",
                introspectionResult.Reason);
            await RejectRequest(context, "Token has been revoked");
            return;
        }

        await _next(context);
    }

    private async Task<IntrospectionResult> IntrospectTokenAsync(
        string token,
        ZeroTrustAuthOptions options,
        IHttpClientFactory clientFactory)
    {
        if (string.IsNullOrEmpty(options.IntrospectionEndpoint))
        {
            _logger.LogError("Introspection endpoint not configured");
            return new IntrospectionResult(false, "Introspection endpoint not configured");
        }

        try
        {
            HttpClient client = clientFactory.CreateClient("KeycloakIntrospection");

            // RFC 7662 compliant introspection request
            var request = new HttpRequestMessage(HttpMethod.Post, options.IntrospectionEndpoint);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token, ["client_id"] = options.ClientId, ["client_secret"] = options.ClientSecret
            });
            request.Content = content;

            HttpResponseMessage response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Introspection request failed with status {StatusCode}", response.StatusCode);
                // Fail-open in case of Keycloak unavailability (configurable)
                return new IntrospectionResult(true, "Introspection endpoint unavailable - fail-open");
            }

            string responseContent = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseContent);

            // Keycloak returns { "active": false } if token is revoked/invalid
            if (!jsonDoc.RootElement.TryGetProperty("active", out JsonElement active) || !active.GetBoolean())
                return new IntrospectionResult(false, "Token marked as inactive by identity provider");

            return new IntrospectionResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token introspection");
            // Fail-open on exception to prevent total lockout
            return new IntrospectionResult(true, "Introspection failed with exception - fail-open");
        }
    }

    private static async Task RejectRequest(HttpContext context, string reason)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            title = "Unauthorized",
            status = 401,
            detail = reason,
            instance = context.Request.Path.Value
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    }

    private static string ComputeTokenHash(string token)
    {
        // Use first/last parts of token to create a short cache key
        // Full cryptographic hash not needed since this is just for cache keying
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash)[..16];
    }

    private readonly record struct IntrospectionResult(bool IsActive, string? Reason);
}
