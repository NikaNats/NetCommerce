#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.Domain.Tests.Security;

/// <summary>
///     Unit tests for KeycloakTokenProxy — the BFF proxy that delegates all
///     token lifecycle operations to Keycloak's native endpoints.
///     Verifies:
///     - Authorization code exchange (Auth Code + PKCE)
///     - Client credentials exchange (M2M)
///     - Refresh token rotation (Keycloak native)
///     - Token revocation (RFC 7009)
///     - Logout (revoke + end session)
///     - Error mapping (KC 400→400, KC 401→401, KC 5xx→502)
///     - Unconfigured endpoint handling
///     - Network failure handling
/// </summary>
public class KeycloakTokenProxyTests
{
    private readonly ILogger<KeycloakTokenProxy> _logger = Substitute.For<ILogger<KeycloakTokenProxy>>();

    private readonly ZeroTrustAuthOptions _defaultOptions = new()
    {
        Authority = "http://localhost:8080",
        Realm = "netcommerce",
        ClientId = "netcommerce-api",
        ClientSecret = "test-secret",
        BffClientId = "netcommerce-web",
        ApiScope = "netcommerce.api"
    };

    private KeycloakTokenProxy CreateProxy(HttpMessageHandler handler, ZeroTrustAuthOptions? options = null)
    {
        var httpClient = new HttpClient(handler);
        return new KeycloakTokenProxy(
            httpClient,
            Options.Create(options ?? _defaultOptions),
            _logger);
    }

    private static string SerializeTokenResponse(
        string accessToken = "access-token-123",
        string? refreshToken = "refresh-token-456",
        int expiresIn = 300,
        int refreshExpiresIn = 1800,
        string tokenType = "Bearer",
        string? scope = "openid profile")
    {
        return JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            expires_in = expiresIn,
            refresh_expires_in = refreshExpiresIn,
            token_type = tokenType,
            scope
        });
    }

    private static string SerializeErrorResponse(string error, string? description = null)
    {
        return JsonSerializer.Serialize(new
        {
            error,
            error_description = description
        });
    }

    // ========================================================================
    // Authorization Code Exchange
    // ========================================================================

    [Fact]
    public async Task ExchangeAuthorizationCode_Success_ReturnsTokens()
    {
        // Arrange
        var handler = new FakeHttpHandler(HttpStatusCode.OK, SerializeTokenResponse());
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeAuthorizationCodeAsync(
            code: "auth-code-xyz",
            codeVerifier: "pkce-verifier-abc",
            redirectUri: "http://localhost:3000/callback");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.StatusCode.ShouldBe(200);
        result.TokenResponse.ShouldNotBeNull();
        result.TokenResponse.AccessToken.ShouldBe("access-token-123");
        result.TokenResponse.RefreshToken.ShouldBe("refresh-token-456");
        result.TokenResponse.ExpiresIn.ShouldBe(300);
        result.TokenResponse.TokenType.ShouldBe("Bearer");
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_SendsCorrectFormFields()
    {
        // Arrange
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, SerializeTokenResponse());
        var proxy = CreateProxy(handler);

        // Act
        await proxy.ExchangeAuthorizationCodeAsync(
            code: "test-code",
            codeVerifier: "test-verifier",
            redirectUri: "http://localhost/callback",
            clientId: "netcommerce-web");

        // Assert — verify form fields sent to Keycloak
        handler.CapturedContent.ShouldNotBeNull();
        var formData = await handler.CapturedContent.ReadAsStringAsync();
        formData.ShouldContain("grant_type=authorization_code");
        formData.ShouldContain("code=test-code");
        formData.ShouldContain("code_verifier=test-verifier");
        formData.ShouldContain("client_id=netcommerce-web");
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_ConfidentialClient_IncludesSecret()
    {
        // Arrange — use the API client ID (confidential)
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, SerializeTokenResponse());
        var proxy = CreateProxy(handler);

        // Act
        await proxy.ExchangeAuthorizationCodeAsync(
            code: "code",
            codeVerifier: "verifier",
            redirectUri: "http://localhost/callback",
            clientId: "netcommerce-api"); // confidential client

        // Assert
        var formData = await handler.CapturedContent!.ReadAsStringAsync();
        formData.ShouldContain("client_secret=test-secret");
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_PublicClient_NoSecret()
    {
        // Arrange — default BFF client (public)
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, SerializeTokenResponse());
        var proxy = CreateProxy(handler);

        // Act
        await proxy.ExchangeAuthorizationCodeAsync(
            code: "code",
            codeVerifier: "verifier",
            redirectUri: "http://localhost/callback");

        // Assert
        var formData = await handler.CapturedContent!.ReadAsStringAsync();
        formData.ShouldNotContain("client_secret");
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_InvalidCode_Returns400()
    {
        // Arrange
        var errorBody = SerializeErrorResponse("invalid_grant", "Code not valid");
        var handler = new FakeHttpHandler(HttpStatusCode.BadRequest, errorBody);
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeAuthorizationCodeAsync(
            code: "expired-code",
            codeVerifier: "verifier",
            redirectUri: "http://localhost/callback");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(400);
        result.Error.ShouldBe("invalid_grant");
        result.ErrorDescription.ShouldBe("Code not valid");
    }

    // ========================================================================
    // Client Credentials Exchange
    // ========================================================================

    [Fact]
    public async Task ExchangeClientCredentials_Success_ReturnsAccessToken()
    {
        // Arrange
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            SerializeTokenResponse(refreshToken: null, scope: "netcommerce.api"));
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.TokenResponse.ShouldNotBeNull();
        result.TokenResponse.AccessToken.ShouldBe("access-token-123");
        result.TokenResponse.RefreshToken.ShouldBeNull();
    }

    [Fact]
    public async Task ExchangeClientCredentials_SendsCredentials()
    {
        // Arrange
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, SerializeTokenResponse());
        var proxy = CreateProxy(handler);

        // Act
        await proxy.ExchangeClientCredentialsAsync();

        // Assert
        var formData = await handler.CapturedContent!.ReadAsStringAsync();
        formData.ShouldContain("grant_type=client_credentials");
        formData.ShouldContain("client_id=netcommerce-api");
        formData.ShouldContain("client_secret=test-secret");
        formData.ShouldContain("scope=netcommerce.api");
    }

    [Fact]
    public async Task ExchangeClientCredentials_InvalidSecret_Returns401()
    {
        // Arrange
        var errorBody = SerializeErrorResponse("unauthorized_client", "Invalid client credentials");
        var handler = new FakeHttpHandler(HttpStatusCode.Unauthorized, errorBody);
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(401);
        result.Error.ShouldBe("unauthorized_client");
    }

    // ========================================================================
    // Refresh Token
    // ========================================================================

    [Fact]
    public async Task RefreshToken_Success_ReturnsRotatedTokens()
    {
        // Arrange
        var handler = new FakeHttpHandler(HttpStatusCode.OK,
            SerializeTokenResponse(
                accessToken: "new-access",
                refreshToken: "new-refresh"));
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.RefreshTokenAsync("old-refresh-token");

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.TokenResponse!.AccessToken.ShouldBe("new-access");
        result.TokenResponse.RefreshToken.ShouldBe("new-refresh");
    }

    [Fact]
    public async Task RefreshToken_ReplayedToken_Returns400()
    {
        // Arrange — Keycloak detects replay and returns invalid_grant
        var errorBody = SerializeErrorResponse("invalid_grant", "Token is not active");
        var handler = new FakeHttpHandler(HttpStatusCode.BadRequest, errorBody);
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.RefreshTokenAsync("replayed-old-token");

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(400);
        result.Error.ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task RefreshToken_UsesDefaultBffClientId()
    {
        // Arrange
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, SerializeTokenResponse());
        var proxy = CreateProxy(handler);

        // Act
        await proxy.RefreshTokenAsync("token");

        // Assert
        var formData = await handler.CapturedContent!.ReadAsStringAsync();
        formData.ShouldContain("client_id=netcommerce-web");
        formData.ShouldNotContain("client_secret");
    }

    // ========================================================================
    // Token Revocation (RFC 7009)
    // ========================================================================

    [Fact]
    public async Task RevokeToken_Success_ReturnsSucceeded()
    {
        // Arrange
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "");
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.RevokeTokenAsync("token-to-revoke");

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task RevokeToken_SendsCorrectFields()
    {
        // Arrange
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, "");
        var proxy = CreateProxy(handler);

        // Act
        await proxy.RevokeTokenAsync("rt-123", "refresh_token");

        // Assert
        var formData = await handler.CapturedContent!.ReadAsStringAsync();
        formData.ShouldContain("token=rt-123");
        formData.ShouldContain("token_type_hint=refresh_token");
        formData.ShouldContain("client_id=netcommerce-api");
        formData.ShouldContain("client_secret=test-secret");
    }

    [Fact]
    public async Task RevokeToken_EndpointNotConfigured_ReturnsFailed()
    {
        // Arrange — empty Authority means no RevocationEndpoint
        var options = new ZeroTrustAuthOptions { Authority = "", Realm = "" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "");
        var proxy = CreateProxy(handler, options);

        // Act
        var result = await proxy.RevokeTokenAsync("token");

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("revocation_endpoint_not_configured");
    }

    [Fact]
    public async Task RevokeToken_NetworkFailure_ReturnsFailed()
    {
        // Arrange
        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused"));
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.RevokeTokenAsync("token");

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("service_unavailable");
    }

    // ========================================================================
    // Logout (Revoke + End Session)
    // ========================================================================

    [Fact]
    public async Task Logout_Success_ReturnsSucceeded()
    {
        // Arrange — both revoke and end-session succeed
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "");
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.LogoutAsync("refresh-token");

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Logout_EndSessionReturns204_StillSucceeds()
    {
        // Arrange
        var handler = new SequentialHttpHandler(
            (HttpStatusCode.OK, ""),      // revoke
            (HttpStatusCode.NoContent, "") // end-session
        );
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.LogoutAsync("refresh-token");

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    // ========================================================================
    // Error Mapping
    // ========================================================================

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.InternalServerError, 502)]
    [InlineData(HttpStatusCode.BadGateway, 502)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 502)]
    public async Task TokenEndpoint_MapsKeycloakStatusCodeCorrectly(
        HttpStatusCode keycloakStatus, int expectedClientStatus)
    {
        // Arrange
        var errorBody = SerializeErrorResponse("test_error");
        var handler = new FakeHttpHandler(keycloakStatus, errorBody);
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(expectedClientStatus);
    }

    [Fact]
    public async Task TokenEndpoint_NonJsonError_UsesRawBody()
    {
        // Arrange — Keycloak sometimes returns HTML errors
        var handler = new FakeHttpHandler(HttpStatusCode.BadGateway, "<html>502</html>");
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(502);
        result.ErrorDescription!.ShouldContain("502");
    }

    [Fact]
    public async Task TokenEndpoint_NotConfigured_ReturnsServerError()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "", Realm = "" };
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "");
        var proxy = CreateProxy(handler, options);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(500);
        result.Error.ShouldBe("server_error");
    }

    [Fact]
    public async Task TokenEndpoint_NetworkFailure_Returns502()
    {
        // Arrange
        var handler = new ThrowingHttpHandler(new HttpRequestException("DNS failure"));
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(502);
        result.Error.ShouldBe("service_unavailable");
    }

    [Fact]
    public async Task TokenEndpoint_UnparseableResponse_Returns502()
    {
        // Arrange — 200 but body is "null" which deserializes to null (not a token)
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "null");
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(502);
        result.Error.ShouldBe("invalid_response");
    }

    [Fact]
    public async Task TokenEndpoint_InvalidJsonBody_Returns502_ServiceUnavailable()
    {
        // Arrange — 200 but body is not valid JSON at all (throws during deserialization)
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "not-json-at-all");
        var proxy = CreateProxy(handler);

        // Act
        var result = await proxy.ExchangeClientCredentialsAsync();

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(502);
        result.Error.ShouldBe("service_unavailable");
    }

    // ========================================================================
    // Test helpers — fake HTTP handlers
    // ========================================================================

    /// <summary>Returns a fixed response for every request.</summary>
    private sealed class FakeHttpHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
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

    /// <summary>Captures the request body for assertion and returns a fixed response.</summary>
    private sealed class CapturingHttpHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpContent? CapturedContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Read and store a copy of the content (since it's disposed after send)
            if (request.Content is not null)
            {
                var raw = await request.Content.ReadAsStringAsync(cancellationToken);
                CapturedContent = new StringContent(raw);
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>Returns sequential responses (for multi-step operations like logout).</summary>
    private sealed class SequentialHttpHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

        public SequentialHttpHandler(params (HttpStatusCode, string)[] responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.OK, "");

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Throws an exception to simulate network failures.</summary>
    private sealed class ThrowingHttpHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw exception;
        }
    }
}
