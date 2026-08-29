#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Kernel.Security.Authorization;

/// <summary>
///     Requirement for admin elevated authentication.
///     Admin endpoints require MORE than just the admin role claim:
///     the security posture defined in <see cref="AdminElevatedAuthOptions"/>
///     determines which additional factors are required.
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
///     FAIL-CLOSED DESIGN: If the API key is not configured,
///     ALL elevated requests are denied regardless of SecurityMode.
///     There is NO silent fallthrough to a weaker authentication factor.
/// </summary>
public sealed class AdminElevatedAuthorizationHandler
    : AuthorizationHandler<AdminElevatedRequirement>
{
    private readonly AdminElevatedAuthOptions _options;
    private readonly ILogger<AdminElevatedAuthorizationHandler> _logger;

    public AdminElevatedAuthorizationHandler(
        IOptions<AdminElevatedAuthOptions> options,
        ILogger<AdminElevatedAuthorizationHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminElevatedRequirement requirement)
    {
        // ── GATE 1: Must have admin role ──
        if (!context.User.IsInRole("admin") && !context.User.IsInRole("Admin"))
        {
            _logger.LogDebug("AdminElevated denied: user lacks admin role.");
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirst("sub")?.Value ?? "unknown";

        // ── GATE 2: API key must be configured (fail-closed) ──
        if (!_options.IsApiKeyConfigured
            && _options.SecurityMode != AdminElevatedSecurityMode.DevelopmentOnly)
        {
            _logger.LogCritical(
                "AdminElevated DENIED for user {UserId}: API key is not configured. Elevated admin operations are inaccessible. Set 'Auth:AdminElevated:ApiKey' (≥ 32 chars) to enable.",
                userId);
            return Task.CompletedTask;
        }

        if (!_options.IsApiKeyConfigured)
        {
            _logger.LogWarning(
                "AdminElevated using DevelopmentOnly mode for user {UserId}. API key is not configured. This mode must not be used in production.",
                userId);
        }

        // ── GATE 3: Evaluate authentication factors based on security mode ──
        var hasValidApiKey = HasValidApiKey(context);
        var hasFreshAuth = HasFreshAuthTime(context, requirement);

        var granted = _options.SecurityMode switch
        {
            AdminElevatedSecurityMode.Strict => hasValidApiKey && hasFreshAuth,
            AdminElevatedSecurityMode.Flexible => hasValidApiKey || hasFreshAuth,
            AdminElevatedSecurityMode.DevelopmentOnly => hasFreshAuth,
            _ => false
        };

        if (granted)
        {
            _logger.LogInformation(
                "AdminElevated GRANTED for user {UserId}. Mode={Mode}, ApiKeyValid={ApiKey}, AuthFresh={AuthFresh}",
                userId, _options.SecurityMode, hasValidApiKey, hasFreshAuth);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "AdminElevated DENIED for user {UserId}. Mode={Mode}, ApiKeyValid={ApiKey}, AuthFresh={AuthFresh}. Requirements: {Requirements}",
                userId, _options.SecurityMode, hasValidApiKey, hasFreshAuth,
                DescribeRequirements(_options.SecurityMode));
        }

        return Task.CompletedTask;
    }

    private bool HasValidApiKey(AuthorizationHandlerContext context)
    {
        if (!_options.IsApiKeyConfigured)
            return false;

        if (context.Resource is not HttpContext httpContext)
            return false;

        var providedKey = httpContext.Request.Headers["X-Admin-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(providedKey))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedKey),
            Encoding.UTF8.GetBytes(_options.ApiKey));
    }

    private bool HasFreshAuthTime(
        AuthorizationHandlerContext context,
        AdminElevatedRequirement requirement)
    {
        var authTimeClaim = context.User.FindFirst("auth_time")?.Value;
        if (string.IsNullOrEmpty(authTimeClaim))
            return false;

        if (!long.TryParse(authTimeClaim, out var authTimeEpoch))
            return false;

        var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeEpoch);
        var maxAge = TimeSpan.FromMinutes(requirement.MaxAuthAgeMinutes);
        return DateTimeOffset.UtcNow - authTime <= maxAge;
    }

    private static string DescribeRequirements(AdminElevatedSecurityMode mode) => mode switch
    {
        AdminElevatedSecurityMode.Strict => "Valid API key AND fresh auth_time required",
        AdminElevatedSecurityMode.Flexible => "Valid API key OR fresh auth_time required",
        AdminElevatedSecurityMode.DevelopmentOnly => "Fresh auth_time required (development only)",
        _ => "Unknown mode — access denied"
    };
}

/// <summary>
///     Endpoint filter for admin elevated operations.
///     Returns a clear 403 with instructions when elevation is required.
///     Fail-closed: unconfigured API key denies all requests except DevelopmentOnly.
/// </summary>
public sealed class AdminElevatedEndpointFilter : IEndpointFilter
{
    private readonly AdminElevatedAuthOptions _options;
    private readonly ILogger<AdminElevatedEndpointFilter> _logger;

    public AdminElevatedEndpointFilter(
        IOptions<AdminElevatedAuthOptions> options,
        ILogger<AdminElevatedEndpointFilter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // Backward compat overload
    public AdminElevatedEndpointFilter(
        IOptions<AdminApiKeyOptions> apiKeyOptions,
        int maxAuthAgeMinutes = 15)
    {
        _options = new AdminElevatedAuthOptions
        {
            ApiKey = apiKeyOptions.Value.ApiKey,
            MaxAuthAgeMinutes = maxAuthAgeMinutes,
            SecurityMode = AdminElevatedSecurityMode.Strict
        };
        _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminElevatedEndpointFilter>.Instance;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.User.IsInRole("admin") && !httpContext.User.IsInRole("Admin"))
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "Admin role is required.",
                statusCode: 403,
                type: "https://docs.netcommerce.io/errors/admin-required");
        }

        var userId = httpContext.User.FindFirst("sub")?.Value ?? "unknown";

        if (!_options.IsApiKeyConfigured
            && _options.SecurityMode != AdminElevatedSecurityMode.DevelopmentOnly)
        {
            _logger.LogCritical(
                "AdminElevated DENIED for user {UserId}: API key is not configured.",
                userId);
            return Results.Problem(
                title: "Elevated Authentication Not Configured",
                detail: "Elevated admin operations are inaccessible: API key not configured.",
                statusCode: 403,
                type: "https://docs.netcommerce.io/errors/admin-elevation-required");
        }

        var hasValidApiKey = HasValidApiKey(httpContext);
        var hasFreshAuth = HasFreshAuthTime(httpContext, _options.MaxAuthAgeMinutes);

        var granted = _options.SecurityMode switch
        {
            AdminElevatedSecurityMode.Strict => hasValidApiKey && hasFreshAuth,
            AdminElevatedSecurityMode.Flexible => hasValidApiKey || hasFreshAuth,
            AdminElevatedSecurityMode.DevelopmentOnly => hasFreshAuth,
            _ => false
        };

        if (granted)
            return await next(context);

        return Results.Problem(
            title: "Step-Up Authentication Required",
            detail: $"This admin operation requires elevated authentication. Requirements: {DescribeRequirements(_options.SecurityMode)}",
            statusCode: 403,
            type: "https://docs.netcommerce.io/errors/admin-elevation-required",
            extensions: new Dictionary<string, object?>
            {
                ["reauthUrl"] = "/connect/authorize?prompt=login&acr_values=urn:mace:incommon:iap:silver"
            });
    }

    private bool HasValidApiKey(HttpContext httpContext)
    {
        if (!_options.IsApiKeyConfigured)
            return false;

        var apiKey = httpContext.Request.Headers["X-Admin-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(apiKey),
            Encoding.UTF8.GetBytes(_options.ApiKey));
    }

    private static bool HasFreshAuthTime(HttpContext httpContext, int maxAuthAgeMinutes)
    {
        var authTimeClaim = httpContext.User.FindFirst("auth_time")?.Value;
        if (string.IsNullOrEmpty(authTimeClaim))
            return false;

        if (!long.TryParse(authTimeClaim, out var authTimeEpoch))
            return false;

        var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeEpoch);
        return DateTimeOffset.UtcNow - authTime <= TimeSpan.FromMinutes(maxAuthAgeMinutes);
    }

    private static string DescribeRequirements(AdminElevatedSecurityMode mode) => mode switch
    {
        AdminElevatedSecurityMode.Strict => "Valid API key AND fresh auth_time required",
        AdminElevatedSecurityMode.Flexible => "Valid API key OR fresh auth_time required",
        AdminElevatedSecurityMode.DevelopmentOnly => "Fresh auth_time required (development only)",
        _ => "Unknown mode — access denied"
    };
}

/// <summary>
///     Configuration for admin API key authentication.
/// </summary>
[Obsolete("Use AdminElevatedAuthOptions instead. Will be removed in v2.0.")]
public sealed class AdminApiKeyOptions
{
    public const string SectionName = "Auth:AdminApiKey";
    public string ApiKey { get; set; } = string.Empty;
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
internal sealed partial class AdminElevationJsonContext : JsonSerializerContext
{
}
