#nullable enable
using System.Text.Json.Serialization;

namespace NetCommerce.Api.Endpoints.Auth;

// ============================================================================
// Request models (moved verbatim from AuthEndpoints.cs — shapes are API contracts
// covered by ApiJsonContext source generation; do not reshape without versioning)
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
