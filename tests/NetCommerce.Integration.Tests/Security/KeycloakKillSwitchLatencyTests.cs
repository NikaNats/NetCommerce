#nullable enable

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Security.Authentication;
using NSubstitute;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace NetCommerce.Integration.Tests.Security;

[Trait("Category", "SecurityZeroTrust")]
public sealed class KeycloakKillSwitchLatencyTests : IAsyncLifetime
{
    private WireMockServer _keycloakMock = null!;
    private HttpClient _httpClient = null!;

    public ValueTask InitializeAsync()
    {
        _keycloakMock = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_keycloakMock.Url!) };
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        _keycloakMock.Stop();
        _keycloakMock.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RevokedToken_MustBeRejected_WithinConfiguredCacheTtl()
    {
        const string testToken = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.compromised_token_payload";
        var cacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        // NOTE: MemoryDistributedCache requires an ILoggerFactory on modern runtimes.
        var memoryCache = new MemoryDistributedCache(cacheOptions, NullLoggerFactory.Instance);

        var authOptions = Options.Create(new ZeroTrustAuthOptions
        {
            Authority = _keycloakMock.Url!,
            Realm = "netcommerce",
            ClientId = "netcommerce-api",
            ClientSecret = "secret",
            IntrospectionEnabled = true,
            IntrospectionCacheSeconds = 2 // Tight 2-second TTL for deterministic test verification
        });

        var clientFactory = Substitute.For<IHttpClientFactory>();
        clientFactory.CreateClient("KeycloakIntrospection").Returns(_httpClient);

        // 1. Initial State: Keycloak says token is ACTIVE
        _keycloakMock.Given(
            Request.Create()
                .WithPath("/realms/netcommerce/protocol/openid-connect/token/introspect")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"active": true, "scope": "netcommerce.api"}"""));

        var middlewareExecuted = false;
        var middleware = new TokenIntrospectionMiddleware(
            next: _ => { middlewareExecuted = true; return Task.CompletedTask; },
            logger: NullLogger<TokenIntrospectionMiddleware>.Instance);

        var context1 = CreateHttpContextWithToken(testToken);

        // 2. First Request: Token active -> Allowed through
        await middleware.InvokeAsync(context1, clientFactory, authOptions, memoryCache);
        middlewareExecuted.ShouldBeTrue("Active token was incorrectly blocked.");

        // 3. ADMINISTRATOR REVOKES TOKEN AT KEYCLOAK (Kill-Switch Pulled)
        _keycloakMock.Reset();
        _keycloakMock.Given(
            Request.Create()
                .WithPath("/realms/netcommerce/protocol/openid-connect/token/introspect")
                .UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"active": false}"""));

        // 4. Request during TTL cache window: Still active from cache
        middlewareExecuted = false;
        var context2 = CreateHttpContextWithToken(testToken);
        await middleware.InvokeAsync(context2, clientFactory, authOptions, memoryCache);
        middlewareExecuted.ShouldBeTrue("Cache should serve active token prior to TTL expiration.");

        // 5. Wait for TTL (2.1 seconds) to expire
        await Task.Delay(2100);

        // 6. Next Request: Cache expired -> Calls Keycloak -> REJECTED (401 Unauthorized)
        middlewareExecuted = false;
        var context3 = CreateHttpContextWithToken(testToken);
        await middleware.InvokeAsync(context3, clientFactory, authOptions, memoryCache);

        middlewareExecuted.ShouldBeFalse("Revoked token was allowed through after introspection TTL expired!");
        context3.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
    }

    private static DefaultHttpContext CreateHttpContextWithToken(string token)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var authService = Substitute.For<IAuthenticationService>();
        var authProperties = new AuthenticationProperties();
        authProperties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);

        authService.AuthenticateAsync(context, Arg.Any<string?>())
            .Returns(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("Bearer")), authProperties, "Bearer")));

        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IAuthenticationService)).Returns(authService);
        context.RequestServices = sp;

        return context;
    }
}
