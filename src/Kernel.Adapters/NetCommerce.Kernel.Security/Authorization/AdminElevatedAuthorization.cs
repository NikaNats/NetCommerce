#nullable enable
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace NetCommerce.Kernel.Security.Authorization;

/// <summary>
///     Requirement for admin elevated authentication.
///     Admin endpoints require MORE than just the admin role claim:
///     1. Recent re-authentication (auth_time within MaxAuthAge), OR
///     2. Valid API key in X-Admin-Api-Key header
///     This defends against session hijacking: even if an attacker steals an admin token,
///     they cannot perform destructive operations without the API key or a fresh login.
/// </summary>
public sealed class AdminElevatedRequirement : IAuthorizationRequirement
{
    /// <summary>
    ///     Maximum age of authentication in minutes.
    ///     If auth_time claim is older than this, re-authentication is required.
    ///     Default: 15 minutes.
    /// </summary>
    public int MaxAuthAgeMinutes { get; init; } = 15;
}

/// <summary>
///     Authorization handler for admin elevated access.
///     Checks the auth_time claim for recency OR validates the X-Admin-Api-Key header.
/// </summary>
public sealed class AdminElevatedAuthorizationHandler
    : AuthorizationHandler<AdminElevatedRequirement>
{
    private readonly AdminApiKeyOptions _apiKeyOptions;

    public AdminElevatedAuthorizationHandler(
        Microsoft.Extensions.Options.IOptions<AdminApiKeyOptions> apiKeyOptions)
    {
        _apiKeyOptions = apiKeyOptions.Value;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminElevatedRequirement requirement)
    {
        // Must have admin role first
        if (!context.User.IsInRole("admin") && !context.User.IsInRole("Admin"))
            return Task.CompletedTask;

        // Method 1: Check for valid API key header
        if (context.Resource is HttpContext httpContext)
        {
            var apiKey = httpContext.Request.Headers["X-Admin-Api-Key"].FirstOrDefault();
            if (!string.IsNullOrEmpty(apiKey) && IsValidApiKey(apiKey))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        // Method 2: Check auth_time for recent re-authentication
        var authTimeClaim = context.User.FindFirst("auth_time")?.Value;
        if (!string.IsNullOrEmpty(authTimeClaim))
        {
            if (long.TryParse(authTimeClaim, out var authTimeEpoch))
            {
                var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeEpoch);
                var maxAge = TimeSpan.FromMinutes(requirement.MaxAuthAgeMinutes);

                if (DateTimeOffset.UtcNow - authTime <= maxAge)
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
        }

        // Neither method satisfied — requirement not met
        return Task.CompletedTask;
    }

    private bool IsValidApiKey(string providedKey)
    {
        if (string.IsNullOrEmpty(_apiKeyOptions.ApiKey))
            return false;

        // Constant-time comparison to prevent timing attacks
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedKey),
            System.Text.Encoding.UTF8.GetBytes(_apiKeyOptions.ApiKey));
    }
}

/// <summary>
///     Endpoint filter for admin elevated operations.
///     Returns a clear 403 with instructions when elevation is required.
///     Applied to admin endpoints that perform destructive operations.
/// </summary>
public sealed class AdminElevatedEndpointFilter : IEndpointFilter
{
    private readonly AdminApiKeyOptions _apiKeyOptions;
    private readonly int _maxAuthAgeMinutes;

    public AdminElevatedEndpointFilter(
        Microsoft.Extensions.Options.IOptions<AdminApiKeyOptions> apiKeyOptions,
        int maxAuthAgeMinutes = 15)
    {
        _apiKeyOptions = apiKeyOptions.Value;
        _maxAuthAgeMinutes = maxAuthAgeMinutes;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Must be admin
        if (!httpContext.User.IsInRole("admin") && !httpContext.User.IsInRole("Admin"))
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "Admin role is required.",
                statusCode: 403,
                type: "https://docs.netcommerce.io/errors/admin-required");
        }

        // Check API key
        var apiKey = httpContext.Request.Headers["X-Admin-Api-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey) && IsValidApiKey(apiKey))
            return await next(context);

        // Check auth_time
        var authTimeClaim = httpContext.User.FindFirst("auth_time")?.Value;
        if (!string.IsNullOrEmpty(authTimeClaim) && long.TryParse(authTimeClaim, out var authTimeEpoch))
        {
            var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeEpoch);
            if (DateTimeOffset.UtcNow - authTime <= TimeSpan.FromMinutes(_maxAuthAgeMinutes))
                return await next(context);
        }

        return Results.Problem(
            title: "Step-Up Authentication Required",
            detail: "This admin operation requires elevated authentication. " +
                    "Either provide a valid X-Admin-Api-Key header or re-authenticate within " +
                    $"the last {_maxAuthAgeMinutes} minutes.",
            statusCode: 403,
            type: "https://docs.netcommerce.io/errors/admin-elevation-required",
            extensions: new Dictionary<string, object?>
            {
                ["reauthUrl"] = "/connect/authorize?prompt=login&acr_values=urn:mace:incommon:iap:silver"
            });
    }

    private bool IsValidApiKey(string providedKey)
    {
        if (string.IsNullOrEmpty(_apiKeyOptions.ApiKey))
            return false;

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(providedKey),
            System.Text.Encoding.UTF8.GetBytes(_apiKeyOptions.ApiKey));
    }
}

/// <summary>
///     Configuration for admin API key authentication.
/// </summary>
public sealed class AdminApiKeyOptions
{
    public const string SectionName = "Auth:AdminApiKey";

    /// <summary>
    ///     The API key value. In production, load from Azure Key Vault or similar.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    ///     Maximum age of authentication before step-up is required (minutes).
    ///     Default: 15 minutes.
    /// </summary>
    public int MaxAuthAgeMinutes { get; set; } = 15;
}

/// <summary>
///     Problem detail response for admin elevation failures (AOT-safe).
/// </summary>
internal sealed class AdminElevationProblem
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = default!;

    [JsonPropertyName("title")]
    public string Title { get; init; } = default!;

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = default!;

    [JsonPropertyName("reauthUrl")]
    public string? ReauthUrl { get; init; }
}

[JsonSerializable(typeof(AdminElevationProblem))]
internal sealed partial class AdminElevationJsonContext : JsonSerializerContext;
