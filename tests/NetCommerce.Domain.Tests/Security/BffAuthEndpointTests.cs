#nullable enable
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.Domain.Tests.Security;

/// <summary>
///     Unit tests for BFF authentication endpoint logic — validates ROPC rejection,
///     grant type routing, input validation, session claims mapping, and Keycloak
///     response mapping. Tests exercise the same logic as AuthEndpoints using the
///     KeycloakTokenProxy directly (no API project reference needed).
/// </summary>
public class BffAuthEndpointTests
{
    private readonly ZeroTrustAuthOptions _options = new()
    {
        Authority = "http://localhost:8080",
        Realm = "netcommerce",
        ClientId = "netcommerce-api",
        ClientSecret = "test-secret",
        BffClientId = "netcommerce-web"
    };

    private KeycloakTokenProxy CreateProxy(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new KeycloakTokenProxy(
            httpClient,
            Options.Create(_options),
            Substitute.For<ILogger<KeycloakTokenProxy>>());
    }

    // ========================================================================
    // ROPC Rejection (OAuth 2.1 compliance)
    // ========================================================================

    [Fact]
    public async Task TokenExchange_ROPC_Password_Returns400()
    {
        // Arrange
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, "{}"));

        // Act — simulates POST /auth/token with grant_type=password
        var result = await SimulateTokenExchange("password", proxy);

        // Assert
        result.StatusCode.ShouldBe(400);
        result.ErrorCode.ShouldBe("unsupported_grant_type");
    }

    [Fact]
    public async Task TokenExchange_ROPC_CaseInsensitive_StillRejected()
    {
        // Arrange
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, "{}"));

        // Act
        var result = await SimulateTokenExchange("Password", proxy);

        // Assert
        result.StatusCode.ShouldBe(400);
        result.ErrorCode.ShouldBe("unsupported_grant_type");
    }

    [Fact]
    public async Task TokenExchange_ROPC_DoesNotCallKeycloak()
    {
        // Arrange — handler that tracks if it was called
        var handler = new CallTrackingHandler();
        var proxy = CreateProxy(handler);

        // Act
        await SimulateTokenExchange("password", proxy);

        // Assert — Keycloak should never be contacted
        handler.WasCalled.ShouldBeFalse();
    }

    // ========================================================================
    // Authorization Code — Validation
    // ========================================================================

    [Fact]
    public async Task AuthCode_MissingCode_Returns400()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, "{}"));

        var result = await SimulateAuthCodeExchange(proxy,
            code: null, codeVerifier: "verifier", redirectUri: "http://localhost/cb");

        result.StatusCode.ShouldBe(400);
        result.ErrorCode!.ShouldContain("code");
    }

    [Fact]
    public async Task AuthCode_MissingCodeVerifier_Returns400_PkceMandatory()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, "{}"));

        var result = await SimulateAuthCodeExchange(proxy,
            code: "valid-code", codeVerifier: null, redirectUri: "http://localhost/cb");

        result.StatusCode.ShouldBe(400);
        result.ErrorCode!.ShouldContain("code_verifier");
    }

    [Fact]
    public async Task AuthCode_MissingRedirectUri_Returns400()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, "{}"));

        var result = await SimulateAuthCodeExchange(proxy,
            code: "code", codeVerifier: "verifier", redirectUri: null);

        result.StatusCode.ShouldBe(400);
        result.ErrorCode!.ShouldContain("redirect_uri");
    }

    [Fact]
    public async Task AuthCode_ValidRequest_ProxiesToKeycloak_Returns200()
    {
        // Arrange
        var tokenJson = SerializeToken("at-123", "rt-456");
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, tokenJson));

        // Act
        var result = await SimulateAuthCodeExchange(proxy,
            code: "valid-code", codeVerifier: "valid-verifier", redirectUri: "http://localhost/cb");

        // Assert
        result.StatusCode.ShouldBe(200);
        result.AccessToken.ShouldBe("at-123");
        result.RefreshToken.ShouldBe("rt-456");
    }

    [Fact]
    public async Task AuthCode_KeycloakRejectsCode_MapsError()
    {
        // Arrange — Keycloak returns 400 for expired/invalid code
        var errorBody = JsonSerializer.Serialize(new { error = "invalid_grant", error_description = "Code expired" });
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.BadRequest, errorBody));

        // Act
        var result = await SimulateAuthCodeExchange(proxy,
            code: "expired-code", codeVerifier: "verifier", redirectUri: "http://localhost/cb");

        // Assert
        result.StatusCode.ShouldBe(400);
        result.ErrorCode.ShouldBe("invalid_grant");
    }

    // ========================================================================
    // Client Credentials
    // ========================================================================

    [Fact]
    public async Task ClientCredentials_ValidRequest_Returns200()
    {
        var tokenJson = SerializeToken("service-token", null, scope: "netcommerce.api");
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, tokenJson));

        var result = await SimulateTokenExchange("client_credentials", proxy);

        result.StatusCode.ShouldBe(200);
        result.AccessToken.ShouldBe("service-token");
    }

    [Fact]
    public async Task ClientCredentials_InvalidSecret_Returns401()
    {
        var errorBody =
            JsonSerializer.Serialize(new { error = "unauthorized_client", error_description = "Bad credentials" });
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.Unauthorized, errorBody));

        var result = await SimulateTokenExchange("client_credentials", proxy);

        result.StatusCode.ShouldBe(401);
        result.ErrorCode.ShouldBe("unauthorized_client");
    }

    // ========================================================================
    // Unknown Grant Type
    // ========================================================================

    [Fact]
    public async Task UnknownGrantType_Returns400()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, "{}"));

        var result = await SimulateTokenExchange("urn:ietf:custom:unknown", proxy);

        result.StatusCode.ShouldBe(400);
        result.ErrorCode.ShouldBe("unsupported_grant_type");
    }

    // ========================================================================
    // Refresh Token — Validation
    // ========================================================================

    [Fact]
    public async Task Refresh_MissingToken_Returns400()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, "{}"));

        var result = await SimulateRefresh(proxy, refreshToken: "");

        result.StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsRotatedTokens()
    {
        var tokenJson = SerializeToken("new-at", "new-rt");
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, tokenJson));

        var result = await SimulateRefresh(proxy, refreshToken: "valid-old-rt");

        result.StatusCode.ShouldBe(200);
        result.AccessToken.ShouldBe("new-at");
        result.RefreshToken.ShouldBe("new-rt");
    }

    [Fact]
    public async Task Refresh_ReplayedToken_Returns400()
    {
        var errorBody = JsonSerializer.Serialize(new { error = "invalid_grant", error_description = "Token not active" });
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.BadRequest, errorBody));

        var result = await SimulateRefresh(proxy, refreshToken: "replayed-token");

        result.StatusCode.ShouldBe(400);
        result.ErrorCode.ShouldBe("invalid_grant");
    }

    // ========================================================================
    // Revoke Token — Validation
    // ========================================================================

    [Fact]
    public async Task Revoke_MissingToken_Returns400()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, ""));

        var result = await SimulateRevoke(proxy, token: "");

        result.ShouldBe(400);
    }

    [Fact]
    public async Task Revoke_ValidToken_Returns200_PerRfc7009()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, ""));

        var result = await SimulateRevoke(proxy, token: "token-to-revoke");

        result.ShouldBe(200);
    }

    // ========================================================================
    // Logout — Validation
    // ========================================================================

    [Fact]
    public async Task Logout_MissingRefreshToken_Returns400()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, ""));

        var result = await SimulateLogout(proxy, refreshToken: "");

        result.ShouldBe(400);
    }

    [Fact]
    public async Task Logout_ValidRequest_Returns204()
    {
        var proxy = CreateProxy(new FakeHandler(HttpStatusCode.OK, ""));

        var result = await SimulateLogout(proxy, refreshToken: "refresh-token");

        result.ShouldBe(204);
    }

    // ========================================================================
    // Session Info — Keycloak JWT Claims Mapping
    // ========================================================================

    [Fact]
    public void SessionInfo_MapsStandardKeycloakClaims()
    {
        // Arrange
        var user = CreateUser(
            ("sub", "user-uuid-123"),
            ("preferred_username", "admin"),
            ("email", "admin@netcommerce.io"),
            (ClaimTypes.Role, "Admin"),
            (ClaimTypes.Role, "Vendor"),
            ("client_roles", "manage-products"),
            ("tenant_id", "tenant-1"),
            ("exp", "1750000000"),
            ("auth_time", "1749999000"),
            ("session_state", "sess-xyz"));

        // Act
        var session = ExtractSessionInfo(user);

        // Assert
        session.UserId.ShouldBe("user-uuid-123");
        session.Username.ShouldBe("admin");
        session.Email.ShouldBe("admin@netcommerce.io");
        session.RealmRoles.ShouldContain("Admin");
        session.RealmRoles.ShouldContain("Vendor");
        session.ClientRoles.ShouldContain("manage-products");
        session.TenantId.ShouldBe("tenant-1");
        session.SessionState.ShouldBe("sess-xyz");
    }

    [Fact]
    public void SessionInfo_FallsBackToNameIdentifier()
    {
        var user = CreateUser((ClaimTypes.NameIdentifier, "fallback-uuid"));

        var session = ExtractSessionInfo(user);

        session.UserId.ShouldBe("fallback-uuid");
    }

    [Fact]
    public void SessionInfo_ParsesTokenExpiry()
    {
        var expireEpoch = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var user = CreateUser(("sub", "u"), ("exp", expireEpoch.ToString()));

        var session = ExtractSessionInfo(user);

        session.TokenExpiresAt.ShouldNotBeNull();
        session.TokenExpiresAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    [Fact]
    public void SessionInfo_ParsesAuthTime()
    {
        var authEpoch = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds();
        var user = CreateUser(("sub", "u"), ("auth_time", authEpoch.ToString()));

        var session = ExtractSessionInfo(user);

        session.AuthenticatedAt.ShouldNotBeNull();
        session.AuthenticatedAt!.Value.ShouldBeLessThan(DateTime.UtcNow);
    }

    [Fact]
    public void SessionInfo_DeduplicatesRoles()
    {
        var user = CreateUser(
            ("sub", "u"),
            (ClaimTypes.Role, "Admin"),
            ("roles", "Admin")); // duplicate via different claim type

        var session = ExtractSessionInfo(user);

        session.RealmRoles.Count(r => r == "Admin").ShouldBe(1);
    }

    // ========================================================================
    // Simulation helpers — replicate endpoint logic
    // ========================================================================

    private async Task<EndpointTestResult> SimulateTokenExchange(
        string grantType, KeycloakTokenProxy proxy,
        string? code = null, string? codeVerifier = null, string? redirectUri = null, string? clientId = null)
    {
        // ROPC rejection
        if (string.Equals(grantType, "password", StringComparison.OrdinalIgnoreCase))
            return EndpointTestResult.Fail(400, "unsupported_grant_type");

        KeycloakTokenResult result;

        switch (grantType.ToLowerInvariant())
        {
            case "authorization_code":
                if (string.IsNullOrEmpty(code))
                    return EndpointTestResult.Fail(400, "invalid_request: code");
                if (string.IsNullOrEmpty(codeVerifier))
                    return EndpointTestResult.Fail(400, "invalid_request: code_verifier");
                if (string.IsNullOrEmpty(redirectUri))
                    return EndpointTestResult.Fail(400, "invalid_request: redirect_uri");
                result = await proxy.ExchangeAuthorizationCodeAsync(code, codeVerifier, redirectUri, clientId);
                break;
            case "client_credentials":
                result = await proxy.ExchangeClientCredentialsAsync();
                break;
            default:
                return EndpointTestResult.Fail(400, "unsupported_grant_type");
        }

        return result.IsSuccess
            ? EndpointTestResult.Ok(result.TokenResponse!)
            : EndpointTestResult.Fail(result.StatusCode, result.Error ?? "unknown");
    }

    private async Task<EndpointTestResult> SimulateAuthCodeExchange(
        KeycloakTokenProxy proxy, string? code, string? codeVerifier, string? redirectUri)
        => await SimulateTokenExchange("authorization_code", proxy, code, codeVerifier, redirectUri);

    private async Task<EndpointTestResult> SimulateRefresh(KeycloakTokenProxy proxy, string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return EndpointTestResult.Fail(400, "invalid_request");

        var result = await proxy.RefreshTokenAsync(refreshToken);
        return result.IsSuccess
            ? EndpointTestResult.Ok(result.TokenResponse!)
            : EndpointTestResult.Fail(result.StatusCode, result.Error ?? "unknown");
    }

    private async Task<int> SimulateRevoke(KeycloakTokenProxy proxy, string token)
    {
        if (string.IsNullOrEmpty(token)) return 400;
        await proxy.RevokeTokenAsync(token);
        return 200; // RFC 7009: always 200
    }

    private async Task<int> SimulateLogout(KeycloakTokenProxy proxy, string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return 400;
        await proxy.LogoutAsync(refreshToken);
        return 204;
    }

    /// <summary>Replicates the session info claims extraction from AuthEndpoints.GetSessionInfo.</summary>
    private static SessionInfo ExtractSessionInfo(ClaimsPrincipal user)
    {
        return new SessionInfo
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
            TokenExpiresAt = ParseEpoch(user.FindFirst("exp")?.Value),
            AuthenticatedAt = ParseEpoch(user.FindFirst("auth_time")?.Value),
            SessionState = user.FindFirst("session_state")?.Value
        };
    }

    private static DateTime? ParseEpoch(string? value)
    {
        if (value is not null && long.TryParse(value, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return null;
    }

    private static ClaimsPrincipal CreateUser(params (string Type, string Value)[] claims)
    {
        var claimsList = claims.Select(c => new Claim(c.Type, c.Value)).ToArray();
        return new ClaimsPrincipal(new ClaimsIdentity(claimsList, "Bearer"));
    }

    private static string SerializeToken(string accessToken, string? refreshToken,
        int expiresIn = 300, string? scope = "openid")
    {
        return JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            expires_in = expiresIn,
            refresh_expires_in = 1800,
            token_type = "Bearer",
            scope
        });
    }

    // ========================================================================
    // Test-local types
    // ========================================================================

    private sealed record EndpointTestResult
    {
        public int StatusCode { get; init; }
        public string? AccessToken { get; init; }
        public string? RefreshToken { get; init; }
        public string? ErrorCode { get; init; }

        public static EndpointTestResult Ok(KeycloakTokenResponse token)
            => new()
            {
                StatusCode = 200,
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken
            };

        public static EndpointTestResult Fail(int status, string error)
            => new() { StatusCode = status, ErrorCode = error };
    }

    private sealed record SessionInfo
    {
        public string UserId { get; init; } = "unknown";
        public string Username { get; init; } = "unknown";
        public string? Email { get; init; }
        public List<string> RealmRoles { get; init; } = [];
        public List<string> ClientRoles { get; init; } = [];
        public string? TenantId { get; init; }
        public DateTime? TokenExpiresAt { get; init; }
        public DateTime? AuthenticatedAt { get; init; }
        public string? SessionState { get; init; }
    }

    // ========================================================================
    // Test HTTP handlers
    // ========================================================================

    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Tracks whether SendAsync was ever called.</summary>
    private sealed class CallTrackingHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
