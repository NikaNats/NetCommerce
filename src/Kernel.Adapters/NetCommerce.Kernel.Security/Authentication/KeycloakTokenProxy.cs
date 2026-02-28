#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Kernel.Security.Authentication;

/// <summary>
///     BFF proxy for Keycloak's OAuth 2.0 / OIDC endpoints.
///     The API never issues tokens itself — all token lifecycle management is delegated to Keycloak.
///     This service proxies:
///     - Authorization Code exchange (RFC 6749 §4.1.3)
///     - Client Credentials grant (RFC 6749 §4.4)
///     - Refresh Token grant with rotation (RFC 6749 §6, Keycloak native)
///     - Token Revocation (RFC 7009)
///     - RP-Initiated Logout (OIDC)
/// </summary>
public sealed class KeycloakTokenProxy
{
    private readonly HttpClient _httpClient;
    private readonly ZeroTrustAuthOptions _options;
    private readonly ILogger<KeycloakTokenProxy> _logger;

    public KeycloakTokenProxy(
        HttpClient httpClient,
        IOptions<ZeroTrustAuthOptions> options,
        ILogger<KeycloakTokenProxy> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    ///     Exchanges an authorization code for tokens (Auth Code + PKCE flow).
    ///     The code was obtained by the SPA from Keycloak's /authorize endpoint.
    ///     The API proxies the exchange so the client_secret (for confidential clients)
    ///     never reaches the browser.
    /// </summary>
    public async Task<KeycloakTokenResult> ExchangeAuthorizationCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        string? clientId = null,
        CancellationToken ct = default)
    {
        var effectiveClientId = clientId ?? _options.BffClientId;

        // For public clients (netcommerce-web), no client_secret is sent.
        // For confidential clients (netcommerce-api), include the secret.
        var formFields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = effectiveClientId
        };

        // Only include client_secret for the confidential API client
        if (string.Equals(effectiveClientId, _options.ClientId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(_options.ClientSecret))
        {
            formFields["client_secret"] = _options.ClientSecret;
        }

        _logger.LogInformation(
            "Exchanging authorization code for client {ClientId}",
            effectiveClientId);

        return await PostToTokenEndpointAsync(formFields, ct);
    }

    /// <summary>
    ///     Exchanges client credentials for an access token (M2M flow).
    ///     Uses the confidential API client credentials from configuration.
    /// </summary>
    public async Task<KeycloakTokenResult> ExchangeClientCredentialsAsync(
        CancellationToken ct = default)
    {
        var formFields = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["scope"] = _options.ApiScope
        };

        _logger.LogInformation("Exchanging client credentials for {ClientId}", _options.ClientId);

        return await PostToTokenEndpointAsync(formFields, ct);
    }

    /// <summary>
    ///     Refreshes tokens using Keycloak's native refresh token rotation.
    ///     Keycloak invalidates the old refresh token and issues a new one (when revokeRefreshToken=true).
    ///     Replaying an old refresh token triggers family-wide revocation at Keycloak.
    /// </summary>
    public async Task<KeycloakTokenResult> RefreshTokenAsync(
        string refreshToken,
        string? clientId = null,
        CancellationToken ct = default)
    {
        var effectiveClientId = clientId ?? _options.BffClientId;

        var formFields = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = effectiveClientId
        };

        // Include client_secret for confidential clients
        if (string.Equals(effectiveClientId, _options.ClientId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(_options.ClientSecret))
        {
            formFields["client_secret"] = _options.ClientSecret;
        }

        _logger.LogDebug("Refreshing token for client {ClientId}", effectiveClientId);

        return await PostToTokenEndpointAsync(formFields, ct);
    }

    /// <summary>
    ///     Revokes a token at Keycloak's RFC 7009 revocation endpoint.
    ///     Per the spec, revocation is always 200 OK (idempotent).
    /// </summary>
    public async Task<KeycloakOperationResult> RevokeTokenAsync(
        string token,
        string tokenTypeHint = "refresh_token",
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.RevocationEndpoint))
        {
            return KeycloakOperationResult.Failed(
                "revocation_endpoint_not_configured",
                "Keycloak realm URL is not configured.");
        }

        var formFields = new Dictionary<string, string>
        {
            ["token"] = token,
            ["token_type_hint"] = tokenTypeHint,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret
        };

        try
        {
            using var content = new FormUrlEncodedContent(formFields);
            var response = await _httpClient.PostAsync(_options.RevocationEndpoint, content, ct);

            // RFC 7009: revocation endpoint always returns 200 for valid requests
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Token revoked successfully");
                return KeycloakOperationResult.Success();
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Token revocation returned {StatusCode}: {Body}",
                (int)response.StatusCode, body);

            return KeycloakOperationResult.Failed(
                "revocation_failed",
                $"Keycloak returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keycloak revocation endpoint unreachable");
            return KeycloakOperationResult.Failed(
                "service_unavailable",
                "The authentication service is temporarily unavailable.");
        }
    }

    /// <summary>
    ///     Ends the Keycloak session (RP-Initiated Logout).
    ///     Revokes the refresh token first, then calls the end-session endpoint
    ///     to terminate the server-side session and invalidate all tokens in it.
    /// </summary>
    public async Task<KeycloakOperationResult> LogoutAsync(
        string refreshToken,
        CancellationToken ct = default)
    {
        // Step 1: Revoke the refresh token
        var revokeResult = await RevokeTokenAsync(refreshToken, "refresh_token", ct);
        if (!revokeResult.Succeeded)
        {
            _logger.LogWarning(
                "Revocation during logout failed (continuing): {Error}",
                revokeResult.ErrorDescription);
        }

        // Step 2: End the Keycloak session
        if (string.IsNullOrEmpty(_options.EndSessionEndpoint))
        {
            return revokeResult; // Best effort — revocation was attempted
        }

        var formFields = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken
        };

        try
        {
            using var content = new FormUrlEncodedContent(formFields);
            var response = await _httpClient.PostAsync(_options.EndSessionEndpoint, content, ct);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogInformation("Keycloak session ended successfully");
                return KeycloakOperationResult.Success();
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Keycloak end-session returned {StatusCode}: {Body}",
                (int)response.StatusCode, body);

            // Still consider this a success if revocation worked
            return revokeResult.Succeeded
                ? KeycloakOperationResult.Success()
                : KeycloakOperationResult.Failed("logout_partial", "Token revoked but session end failed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keycloak end-session endpoint unreachable");
            return revokeResult.Succeeded
                ? KeycloakOperationResult.Success()
                : KeycloakOperationResult.Failed("service_unavailable",
                    "The authentication service is temporarily unavailable.");
        }
    }

    // ========================================================================
    // Private helpers
    // ========================================================================

    private async Task<KeycloakTokenResult> PostToTokenEndpointAsync(
        Dictionary<string, string> formFields,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.TokenEndpoint))
        {
            return KeycloakTokenResult.Failed(500, "server_error",
                "Token endpoint not configured. Check Keycloak connection.");
        }

        try
        {
            using var content = new FormUrlEncodedContent(formFields);
            var response = await _httpClient.PostAsync(_options.TokenEndpoint, content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize(
                    responseBody,
                    KeycloakProxyJsonContext.Default.KeycloakTokenResponse);

                if (tokenResponse is null)
                {
                    return KeycloakTokenResult.Failed(502, "invalid_response",
                        "Keycloak returned an unparseable token response.");
                }

                return KeycloakTokenResult.Succeeded(tokenResponse);
            }

            // Parse Keycloak error response
            KeycloakErrorResponse? error = null;
            try
            {
                error = JsonSerializer.Deserialize(
                    responseBody,
                    KeycloakProxyJsonContext.Default.KeycloakErrorResponse);
            }
            catch
            {
                // Keycloak returned non-JSON error — use raw body
            }

            var statusCode = (int)response.StatusCode;
            var errorCode = error?.Error ?? "unknown_error";
            var errorDesc = error?.ErrorDescription ?? responseBody;

            _logger.LogWarning(
                "Keycloak token endpoint returned {StatusCode}: {Error} - {Description}",
                statusCode, errorCode, errorDesc);

            // Map Keycloak status codes to appropriate client-facing codes
            var clientStatusCode = statusCode switch
            {
                400 => 400, // Invalid grant, missing params
                401 => 401, // Invalid client credentials
                403 => 403, // Client not allowed
                _ when statusCode >= 500 => 502, // Upstream error → Bad Gateway
                _ => statusCode
            };

            return KeycloakTokenResult.Failed(clientStatusCode, errorCode, errorDesc);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Genuine cancellation — let it propagate
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Keycloak token endpoint unreachable");
            return KeycloakTokenResult.Failed(502, "service_unavailable",
                "The authentication service is temporarily unavailable.");
        }
    }
}

// ============================================================================
// Result types
// ============================================================================

/// <summary>
///     Result of a token exchange/refresh operation against Keycloak.
/// </summary>
public sealed record KeycloakTokenResult
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public KeycloakTokenResponse? TokenResponse { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public static KeycloakTokenResult Succeeded(KeycloakTokenResponse response)
        => new() { IsSuccess = true, StatusCode = 200, TokenResponse = response };

    public static KeycloakTokenResult Failed(int statusCode, string error, string? description = null)
        => new() { IsSuccess = false, StatusCode = statusCode, Error = error, ErrorDescription = description };
}

/// <summary>
///     Result of a non-token operation (revoke, logout) against Keycloak.
/// </summary>
public sealed record KeycloakOperationResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public static KeycloakOperationResult Success() => new() { Succeeded = true };

    public static KeycloakOperationResult Failed(string error, string? description = null)
        => new() { Succeeded = false, Error = error, ErrorDescription = description };
}

// ============================================================================
// Keycloak response models (for deserialization)
// ============================================================================

/// <summary>
///     Keycloak's token endpoint success response.
///     Maps to the standard OAuth 2.0 token response (RFC 6749 §5.1).
/// </summary>
public sealed class KeycloakTokenResponse
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

    [JsonPropertyName("session_state")]
    public string? SessionState { get; init; }
}

/// <summary>
///     Keycloak's token endpoint error response (RFC 6749 §5.2).
/// </summary>
public sealed class KeycloakErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = default!;

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}

/// <summary>
///     AOT-safe JSON serialization context for Keycloak proxy types.
/// </summary>
[JsonSerializable(typeof(KeycloakTokenResponse))]
[JsonSerializable(typeof(KeycloakErrorResponse))]
internal sealed partial class KeycloakProxyJsonContext : JsonSerializerContext;
