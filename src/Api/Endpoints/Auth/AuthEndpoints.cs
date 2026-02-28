#nullable enable
using System.Security.Claims;
using System.Text.Json.Serialization;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.Api.Endpoints.Auth;

/// <summary>
///     BFF (Backend for Frontend) authentication endpoints.
///     All token lifecycle management is delegated to Keycloak — the API never issues tokens itself.
///     Endpoints:
///     - POST /auth/token    — Exchange auth code (PKCE) or client credentials for tokens
///     - POST /auth/refresh  — Rotate refresh token (Keycloak native rotation)
///     - POST /auth/revoke   — Revoke a token (RFC 7009)
///     - POST /auth/logout   — End session (revoke + OIDC logout)
///     - GET  /auth/session  — Introspect current user's claims
/// </summary>
public class AuthEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/auth")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Authentication");

        group.MapPost("/token", ExchangeToken)
            .WithName("ExchangeToken")
            .WithSummary("Exchange authorization code or client credentials for tokens via Keycloak")
            .WithDescription(
                "BFF proxy to Keycloak's token endpoint. Supports grant_type=authorization_code " +
                "(with PKCE code_verifier) and grant_type=client_credentials. " +
                "The deprecated password grant (ROPC) is explicitly rejected. " +
                "For SPAs: use Authorization Code + PKCE via Keycloak's /authorize, " +
                "then exchange the code here.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .Produces<AuthErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<AuthErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .RequireRateLimiting("AuthStrict")
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshToken)
            .WithName("RefreshToken")
            .WithSummary("Refresh tokens via Keycloak's native rotation")
            .WithDescription(
                "Exchanges a Keycloak refresh token for a new access/refresh token pair. " +
                "Keycloak handles rotation natively (revokeRefreshToken=true): " +
                "the old refresh token is invalidated, replaying it revokes the entire session.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .Produces<AuthErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting("AuthStrict")
            .AllowAnonymous();

        group.MapPost("/revoke", RevokeToken)
            .WithName("RevokeToken")
            .WithSummary("Revoke a token at Keycloak (RFC 7009)")
            .WithDescription(
                "Proxies to Keycloak's revocation endpoint. " +
                "Use on logout to invalidate refresh tokens. " +
                "Per RFC 7009, the response is always 200 (idempotent).")
            .Produces(StatusCodes.Status200OK)
            .Produces<AuthErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireRateLimiting("AuthStrict")
            .AllowAnonymous();

        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("End Keycloak session and revoke tokens")
            .WithDescription(
                "Revokes the refresh token and ends the Keycloak server-side session (RP-Initiated Logout). " +
                "All tokens in the session are invalidated.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<AuthErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireRateLimiting("AuthStrict")
            .AllowAnonymous();

        group.MapGet("/session", GetSessionInfo)
            .WithName("GetSessionInfo")
            .WithSummary("Get current user's session information from JWT claims")
            .WithDescription(
                "Returns the authenticated user's identity, roles, permissions, " +
                "token expiry, and auth time — all extracted from the Keycloak-issued JWT.")
            .Produces<SessionInfoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
    }

    // ========================================================================
    // POST /auth/token
    // ========================================================================

    private static async Task<IResult> ExchangeToken(
        TokenRequest request,
        KeycloakTokenProxy proxy,
        ILogger<AuthEndpoints> logger)
    {
        // Explicitly reject ROPC (deprecated in OAuth 2.1)
        if (string.Equals(request.GrantType, "password", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Rejected deprecated ROPC grant_type=password request");
            return Results.Json(
                new AuthErrorResponse
                {
                    Error = "unsupported_grant_type",
                    ErrorDescription =
                        "The password grant (ROPC) is deprecated per OAuth 2.1. " +
                        "Use authorization_code with PKCE instead."
                },
                statusCode: 400);
        }

        KeycloakTokenResult result;

        switch (request.GrantType?.ToLowerInvariant())
        {
            case "authorization_code":
                if (string.IsNullOrEmpty(request.Code))
                {
                    return Results.Json(
                        new AuthErrorResponse
                        {
                            Error = "invalid_request",
                            ErrorDescription = "The 'code' parameter is required for authorization_code grant."
                        },
                        statusCode: 400);
                }

                if (string.IsNullOrEmpty(request.CodeVerifier))
                {
                    return Results.Json(
                        new AuthErrorResponse
                        {
                            Error = "invalid_request",
                            ErrorDescription =
                                "The 'code_verifier' parameter is required (PKCE is mandatory)."
                        },
                        statusCode: 400);
                }

                if (string.IsNullOrEmpty(request.RedirectUri))
                {
                    return Results.Json(
                        new AuthErrorResponse
                        {
                            Error = "invalid_request",
                            ErrorDescription = "The 'redirect_uri' parameter is required."
                        },
                        statusCode: 400);
                }

                result = await proxy.ExchangeAuthorizationCodeAsync(
                    request.Code, request.CodeVerifier, request.RedirectUri, request.ClientId);
                break;

            case "client_credentials":
                result = await proxy.ExchangeClientCredentialsAsync();
                break;

            default:
                return Results.Json(
                    new AuthErrorResponse
                    {
                        Error = "unsupported_grant_type",
                        ErrorDescription =
                            $"Unsupported grant_type '{request.GrantType}'. " +
                            "Supported: authorization_code, client_credentials."
                    },
                    statusCode: 400);
        }

        return MapTokenResult(result);
    }

    // ========================================================================
    // POST /auth/refresh
    // ========================================================================

    private static async Task<IResult> RefreshToken(
        RefreshRequest request,
        KeycloakTokenProxy proxy,
        ILogger<AuthEndpoints> logger)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Results.Json(
                new AuthErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "The 'refresh_token' parameter is required."
                },
                statusCode: 400);
        }

        var result = await proxy.RefreshTokenAsync(request.RefreshToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("Token refresh failed: {Error}", result.Error);
        }

        return MapTokenResult(result);
    }

    // ========================================================================
    // POST /auth/revoke
    // ========================================================================

    private static async Task<IResult> RevokeToken(
        RevokeRequest request,
        KeycloakTokenProxy proxy,
        ILogger<AuthEndpoints> logger)
    {
        if (string.IsNullOrEmpty(request.Token))
        {
            return Results.Json(
                new AuthErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "The 'token' parameter is required."
                },
                statusCode: 400);
        }

        var result = await proxy.RevokeTokenAsync(
            request.Token,
            request.TokenTypeHint ?? "refresh_token");

        if (!result.Succeeded)
        {
            logger.LogWarning("Token revocation failed: {Error}", result.Error);
            // Per RFC 7009, still return 200 — revocation is best-effort
        }

        return Results.Ok();
    }

    // ========================================================================
    // POST /auth/logout
    // ========================================================================

    private static async Task<IResult> Logout(
        LogoutRequest request,
        KeycloakTokenProxy proxy,
        ILogger<AuthEndpoints> logger)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Results.Json(
                new AuthErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "The 'refresh_token' parameter is required for logout."
                },
                statusCode: 400);
        }

        var result = await proxy.LogoutAsync(request.RefreshToken);

        if (!result.Succeeded)
        {
            logger.LogWarning("Logout failed: {Error} - {Desc}", result.Error, result.ErrorDescription);
        }

        return Results.NoContent();
    }

    // ========================================================================
    // GET /auth/session
    // ========================================================================

    private static IResult GetSessionInfo(HttpContext httpContext)
    {
        var user = httpContext.User;

        var response = new SessionInfoResponse
        {
            UserId = user.FindFirst("sub")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown",
            Username = user.FindFirst("preferred_username")?.Value
                       ?? user.Identity?.Name ?? "unknown",
            Email = user.FindFirst("email")?.Value,
            RealmRoles = user.FindAll(ClaimTypes.Role)
                .Concat(user.FindAll("roles"))
                .Select(c => c.Value)
                .Distinct()
                .ToList(),
            ClientRoles = user.FindAll("client_roles")
                .Select(c => c.Value)
                .Distinct()
                .ToList(),
            TenantId = user.FindFirst("tenant_id")?.Value ?? user.FindFirst("tid")?.Value,
            TokenExpiresAt = GetTokenExpiry(user),
            AuthenticatedAt = GetAuthTime(user),
            SessionState = user.FindFirst("session_state")?.Value
        };

        return Results.Ok(response);
    }

    // ========================================================================
    // Private helpers
    // ========================================================================

    private static IResult MapTokenResult(KeycloakTokenResult result)
    {
        if (result.IsSuccess && result.TokenResponse is not null)
        {
            return Results.Ok(new TokenResponse
            {
                AccessToken = result.TokenResponse.AccessToken,
                RefreshToken = result.TokenResponse.RefreshToken,
                ExpiresIn = result.TokenResponse.ExpiresIn,
                RefreshExpiresIn = result.TokenResponse.RefreshExpiresIn,
                TokenType = result.TokenResponse.TokenType,
                Scope = result.TokenResponse.Scope
            });
        }

        return Results.Json(
            new AuthErrorResponse
            {
                Error = result.Error ?? "unknown_error",
                ErrorDescription = result.ErrorDescription
            },
            statusCode: result.StatusCode);
    }

    private static DateTime? GetTokenExpiry(ClaimsPrincipal user)
    {
        var exp = user.FindFirst("exp")?.Value;
        if (exp is not null && long.TryParse(exp, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return null;
    }

    private static DateTime? GetAuthTime(ClaimsPrincipal user)
    {
        var authTime = user.FindFirst("auth_time")?.Value;
        if (authTime is not null && long.TryParse(authTime, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return null;
    }
}

// ============================================================================
// Request models
// ============================================================================

/// <summary>
///     Token exchange request. Supports authorization_code (PKCE) and client_credentials grants.
///     The deprecated password grant (ROPC) is explicitly rejected.
/// </summary>
public sealed class TokenRequest
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; init; } = default!;

    /// <summary>Authorization code from Keycloak's /authorize endpoint.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>PKCE code verifier (required when grant_type=authorization_code).</summary>
    [JsonPropertyName("code_verifier")]
    public string? CodeVerifier { get; init; }

    /// <summary>Must match the redirect_uri used in the /authorize request.</summary>
    [JsonPropertyName("redirect_uri")]
    public string? RedirectUri { get; init; }

    /// <summary>
    ///     Optional client_id override. Defaults to the BFF client (netcommerce-web).
    ///     Swagger and CLI clients can specify their own client_id.
    /// </summary>
    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }
}

/// <summary>
///     Refresh token request. Uses the Keycloak-issued refresh token directly.
/// </summary>
public sealed class RefreshRequest
{
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = default!;
}

/// <summary>
///     Token revocation request (RFC 7009).
/// </summary>
public sealed class RevokeRequest
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = default!;

    /// <summary>Hint about the token type. Defaults to "refresh_token".</summary>
    [JsonPropertyName("token_type_hint")]
    public string? TokenTypeHint { get; init; }
}

/// <summary>
///     Logout request — ends the Keycloak session.
/// </summary>
public sealed class LogoutRequest
{
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = default!;
}

// ============================================================================
// Response models
// ============================================================================

/// <summary>
///     Token response — mirrors Keycloak's OAuth 2.0 token response.
///     Returned by /auth/token and /auth/refresh.
/// </summary>
public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = default!;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_expires_in")]
    public int RefreshExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

/// <summary>
///     Session information response — user identity and claims from JWT.
/// </summary>
public sealed class SessionInfoResponse
{
    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = default!;

    [JsonPropertyName("username")]
    public string Username { get; init; } = default!;

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("realm_roles")]
    public List<string> RealmRoles { get; init; } = [];

    [JsonPropertyName("client_roles")]
    public List<string> ClientRoles { get; init; } = [];

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("token_expires_at")]
    public DateTime? TokenExpiresAt { get; init; }

    [JsonPropertyName("authenticated_at")]
    public DateTime? AuthenticatedAt { get; init; }

    [JsonPropertyName("session_state")]
    public string? SessionState { get; init; }
}

/// <summary>
///     OAuth 2.0 error response (RFC 6749 §5.2).
/// </summary>
public sealed class AuthErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = default!;

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}
