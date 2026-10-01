#nullable enable
using NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.Api.Endpoints.Auth;

/// <summary>
///     Token lifecycle handlers (exchange, refresh, revoke, logout). Moved verbatim from
///     AuthEndpoints.cs — per-grant validation messages, ROPC rejection, RFC 7009
///     best-effort revocation, and AOT-safe error shapes are all unchanged.
///     Handler signatures (including ILogger&lt;AuthEndpoints&gt;) are preserved so Minimal
///     API binding, log categories, and OpenAPI metadata behave identically.
/// </summary>
public static class TokenHandlers
{
    public static async Task<IResult> ExchangeToken(
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
                NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
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
                        NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
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
                        NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
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
                        NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
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
                    NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
                    statusCode: 400);
        }

        return MapTokenResult(result);
    }

    public static async Task<IResult> RefreshToken(
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
                NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
                statusCode: 400);
        }

        var result = await proxy.RefreshTokenAsync(request.RefreshToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("Token refresh failed: {Error}", result.Error);
        }

        return MapTokenResult(result);
    }

    public static async Task<IResult> RevokeToken(
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
                NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
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

    public static async Task<IResult> Logout(
        LogoutRequest request,
        KeycloakTokenProxy proxy,
        ILogger<AuthEndpoints> logger)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            // AOT-safe overload: source-generated TypeInfo (IL2026/IL3050).
            return Results.Json(
                new AuthErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "The 'refresh_token' parameter is required for logout."
                },
                NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
                statusCode: 400);
        }

        var result = await proxy.LogoutAsync(request.RefreshToken);

        if (!result.Succeeded)
        {
            logger.LogWarning("Logout failed: {Error} - {Desc}", result.Error, result.ErrorDescription);
        }

        return Results.NoContent();
    }

    public static IResult MapTokenResult(KeycloakTokenResult result)
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

        // AOT-safe overload: source-generated TypeInfo (IL2026/IL3050), same pattern as GlobalExceptionHandler.
        return Results.Json(
            new AuthErrorResponse
            {
                Error = result.Error ?? "unknown_error",
                ErrorDescription = result.ErrorDescription
            },
            NetCommerce.Api.Serialization.ApiJsonContext.Default.AuthErrorResponse,
            statusCode: result.StatusCode);
    }
}
