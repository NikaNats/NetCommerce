#region

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NetCommerce.SharedKernel.Infrastructure.Security.Authentication;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

#endregion

namespace NetCommerce.Integration.Tests.Api;

/// <summary>
///     Integration tests for the Zero-Trust Authentication stack.
///     Uses WireMock to simulate Keycloak endpoints.
/// </summary>
public class ZeroTrustAuthenticationIntegrationTests : IAsyncLifetime
{
    private HttpClient _client = null!;
    private IHost _host = null!;
    private WireMockServer _keycloakMock = null!;
    private string _publicKeyJwk = null!;
    private RSA _rsa = null!;

    public async Task InitializeAsync()
    {
        // Start WireMock to simulate Keycloak
        _keycloakMock = WireMockServer.Start();

        // Generate RSA key pair for JWT signing
        _rsa = RSA.Create(2048);
        _publicKeyJwk = CreateJwksResponse();

        // Setup Keycloak OIDC discovery endpoints
        SetupOidcDiscoveryEndpoints();

        // Build test host
        _host = await CreateTestHost();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _keycloakMock.Stop();
        _keycloakMock.Dispose();
        _rsa.Dispose();
    }

    [Fact]
    public async Task AuthenticatedRequest_WithValidToken_Returns200()
    {
        // Arrange
        string token = CreateValidJwtToken(["customer"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/protected");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithoutToken_Returns401()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/protected");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithExpiredToken_Returns401()
    {
        // Arrange
        string token = CreateExpiredJwtToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/protected");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("Token-Expired", out IEnumerable<string>? values).ShouldBeTrue();
        values!.First().ShouldBe("true");
    }

    [Fact]
    public async Task AuthenticatedRequest_WithInvalidAudience_Returns401()
    {
        // Arrange
        string token = CreateJwtTokenWithWrongAudience();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/protected");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminEndpoint_WithAdminRole_Returns200()
    {
        // Arrange
        string token = CreateValidJwtToken(["admin"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/admin");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminEndpoint_WithCustomerRole_Returns403()
    {
        // Arrange
        string token = CreateValidJwtToken(["customer"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/admin");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RolesClaimsTransformation_FlattensKeycloakRoles()
    {
        // Arrange
        string token = CreateValidJwtToken(["admin", "vendor"], ["catalog:read", "catalog:write"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/roles");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync();
        RolesResponse? roles = JsonSerializer.Deserialize<RolesResponse>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        roles.ShouldNotBeNull();
        roles.Roles.ShouldContain("admin");
        roles.Roles.ShouldContain("vendor");
        roles.Permissions.ShouldContain("catalog:read");
        roles.Permissions.ShouldContain("catalog:write");
    }

    [Fact]
    public async Task TokenIntrospection_WhenTokenRevoked_Returns401()
    {
        // Arrange - Enable introspection for this test
        await _host.StopAsync();
        _host.Dispose();
        _host = await CreateTestHost(true);
        _client = _host.GetTestClient();

        string token = CreateValidJwtToken(["customer"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Setup introspection to return inactive
        _keycloakMock.Given(
                Request.Create()
                    .WithPath("/realms/test/protocol/openid-connect/token/introspect")
                    .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithBody("""{"active": false}""")
                    .WithHeader("Content-Type", "application/json"));

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/protected");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenIntrospection_WhenTokenActive_Returns200()
    {
        // Arrange - Enable introspection for this test
        await _host.StopAsync();
        _host.Dispose();
        _host = await CreateTestHost(true);
        _client = _host.GetTestClient();

        string token = CreateValidJwtToken(["customer"]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Setup introspection to return active
        _keycloakMock.Given(
                Request.Create()
                    .WithPath("/realms/test/protocol/openid-connect/token/introspect")
                    .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithBody("""{"active": true}""")
                    .WithHeader("Content-Type", "application/json"));

        // Act
        HttpResponseMessage response = await _client.GetAsync("/api/test/protected");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<IHost> CreateTestHost(bool introspectionEnabled = false)
    {
        IHostBuilder builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Auth:Authority"] = _keycloakMock.Url,
                        ["Auth:Realm"] = "test",
                        ["Auth:Audience"] = "netcommerce-api",
                        ["Auth:ClientId"] = "netcommerce-api",
                        ["Auth:ClientSecret"] = "test-secret",
                        ["Auth:IntrospectionEnabled"] = introspectionEnabled.ToString(),
                        ["Auth:IntrospectionCacheSeconds"] = "0" // Disable caching for tests
                    });
                });
                webBuilder.ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddAuthorization();

                    // Configure Zero-Trust auth options
                    services.AddOptions<ZeroTrustAuthOptions>()
                        .Bind(context.Configuration.GetSection("Auth"))
                        .ValidateOnStart();

                    // Configure JWT Bearer
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(options =>
                        {
                            options.Authority = $"{_keycloakMock.Url}/realms/test";
                            options.Audience = "netcommerce-api";
                            options.RequireHttpsMetadata = false;
                            options.MapInboundClaims = false;
                            options.SaveToken = true;

                            options.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidateIssuer = true,
                                ValidIssuer = $"{_keycloakMock.Url}/realms/test",
                                ValidateAudience = true,
                                ValidAudience = "netcommerce-api",
                                ValidateLifetime = true,
                                ClockSkew = TimeSpan.FromSeconds(30),
                                RoleClaimType = "roles"
                            };

                            options.Events = new JwtBearerEvents
                            {
                                OnAuthenticationFailed = ctx =>
                                {
                                    if (ctx.Exception is SecurityTokenExpiredException)
                                        ctx.Response.Headers.Append("Token-Expired", "true");
                                    return Task.CompletedTask;
                                }
                            };
                        });

                    // Add claims transformation
                    services.AddTransient<IClaimsTransformation,
                        KeycloakRolesClaimsTransformation>();

                    // Add HTTP client factory for introspection
                    services.AddHttpClient("KeycloakIntrospection");
                    services.AddHttpContextAccessor();

                    services.AddAuthorizationBuilder()
                        .AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();

                    if (introspectionEnabled) app.UseZeroTrustMiddleware();

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/test/protected",
                            async context => { await context.Response.WriteAsync("OK"); }).RequireAuthorization();

                        endpoints.MapGet("/api/test/admin",
                                async context => { await context.Response.WriteAsync("Admin OK"); })
                            .RequireAuthorization("AdminOnly");

                        endpoints.MapGet("/api/test/roles", async context =>
                        {
                            var roles = context.User.Claims
                                .Where(c => c.Type == ClaimTypes.Role)
                                .Select(c => c.Value)
                                .ToList();
                            var permissions = context.User.Claims
                                .Where(c => c.Type == "permissions")
                                .Select(c => c.Value)
                                .ToList();

                            await context.Response.WriteAsJsonAsync(new { roles, permissions });
                        }).RequireAuthorization();
                    });
                });
            });

        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private void SetupOidcDiscoveryEndpoints()
    {
        // OIDC Discovery document
        _keycloakMock.Given(
                Request.Create()
                    .WithPath("/realms/test/.well-known/openid-configuration")
                    .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""
                                {
                                    "issuer": "{{_keycloakMock.Url}}/realms/test",
                                    "authorization_endpoint": "{{_keycloakMock.Url}}/realms/test/protocol/openid-connect/auth",
                                    "token_endpoint": "{{_keycloakMock.Url}}/realms/test/protocol/openid-connect/token",
                                    "introspection_endpoint": "{{_keycloakMock.Url}}/realms/test/protocol/openid-connect/token/introspect",
                                    "userinfo_endpoint": "{{_keycloakMock.Url}}/realms/test/protocol/openid-connect/userinfo",
                                    "jwks_uri": "{{_keycloakMock.Url}}/realms/test/protocol/openid-connect/certs"
                                }
                                """));

        // JWKS endpoint
        _keycloakMock.Given(
                Request.Create()
                    .WithPath("/realms/test/protocol/openid-connect/certs")
                    .UsingGet())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(_publicKeyJwk));
    }

    private string CreateJwksResponse()
    {
        RSAParameters parameters = _rsa.ExportParameters(false);
        string? n = Base64UrlEncoder.Encode(parameters.Modulus);
        string? e = Base64UrlEncoder.Encode(parameters.Exponent);

        return $$"""
                 {
                     "keys": [
                         {
                             "kty": "RSA",
                             "alg": "RS256",
                             "use": "sig",
                             "kid": "test-key-id",
                             "n": "{{n}}",
                             "e": "{{e}}"
                         }
                     ]
                 }
                 """;
    }

    private string CreateValidJwtToken(string[]? roles = null, string[]? clientRoles = null)
    {
        var claims = new List<Claim>
        {
            new("sub", "user-123"), new("preferred_username", "testuser"), new("email", "test@example.com")
        };

        // Add realm_access claim with roles
        if (roles?.Length > 0)
        {
            var realmAccess = new { roles };
            claims.Add(new Claim("realm_access", JsonSerializer.Serialize(realmAccess)));
        }

        // Add resource_access claim with client roles
        if (clientRoles?.Length > 0)
        {
            var resourceAccess = new Dictionary<string, object> { ["netcommerce-api"] = new { roles = clientRoles } };
            claims.Add(new Claim("resource_access", JsonSerializer.Serialize(resourceAccess)));
        }

        return CreateJwtToken(claims, DateTime.UtcNow.AddHours(1));
    }

    private string CreateExpiredJwtToken()
    {
        var claims = new List<Claim> { new("sub", "user-123"), new("preferred_username", "testuser") };

        return CreateJwtToken(claims, DateTime.UtcNow.AddHours(-1));
    }

    private string CreateJwtTokenWithWrongAudience()
    {
        var claims = new List<Claim> { new("sub", "user-123"), new("preferred_username", "testuser") };

        var securityKey = new RsaSecurityKey(_rsa);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            $"{_keycloakMock.Url}/realms/test",
            "wrong-audience",
            claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        token.Header["kid"] = "test-key-id";

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string CreateJwtToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var securityKey = new RsaSecurityKey(_rsa);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            $"{_keycloakMock.Url}/realms/test",
            "netcommerce-api",
            claims,
            expires: expires,
            signingCredentials: credentials);

        token.Header["kid"] = "test-key-id";

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private record RolesResponse(List<string> Roles, List<string> Permissions);
}
