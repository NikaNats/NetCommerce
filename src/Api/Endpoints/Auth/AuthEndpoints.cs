#nullable enable
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Mvc;

namespace NetCommerce.Api.Endpoints.Auth;

/// <summary>
///     BFF (Backend for Frontend) authentication endpoints — routing aggregate only.
///     Handler logic lives in TokenHandlers (token lifecycle) and SessionHandlers
///     (claims mapping); DTOs live in AuthModels. All routes, OpenAPI metadata,
///     rate-limiting, and authorization requirements are unchanged.
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

        group.MapPost("/token", TokenHandlers.ExchangeToken)
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

        group.MapPost("/refresh", TokenHandlers.RefreshToken)
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

        group.MapPost("/revoke", TokenHandlers.RevokeToken)
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

        group.MapPost("/logout", TokenHandlers.Logout)
            .WithName("Logout")
            .WithSummary("End Keycloak session and revoke tokens")
            .WithDescription(
                "Revokes the refresh token and ends the Keycloak server-side session (RP-Initiated Logout). " +
                "All tokens in the session are invalidated.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<AuthErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireRateLimiting("AuthStrict")
            .AllowAnonymous();

        group.MapGet("/session", SessionHandlers.GetSessionInfo)
            .WithName("GetSessionInfo")
            .WithSummary("Get current user's session information from JWT claims")
            .WithDescription(
                "Returns the authenticated user's identity, roles, permissions, " +
                "token expiry, and auth time — all extracted from the Keycloak-issued JWT.")
            .Produces<SessionInfoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
    }
}
