#nullable enable
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.SharedKernel.Infrastructure.Security.Authentication;
using NSubstitute;
using Shouldly;
using Xunit;

namespace NetCommerce.Domain.Tests.Security;

/// <summary>
///     Unit tests for TokenIntrospectionMiddleware (the "Kill Switch").
///     Verifies that token introspection correctly blocks revoked tokens
///     and allows active tokens through.
/// </summary>
public class TokenIntrospectionMiddlewareTests
{
    private readonly TokenIntrospectionMiddleware _middleware;
    private readonly IHttpClientFactory _clientFactory;
    private readonly IOptions<ZeroTrustAuthOptions> _options;
    private readonly IDistributedCache _cache;
    private readonly ILogger<TokenIntrospectionMiddleware> _logger;
    private readonly RequestDelegate _next;
    private bool _nextWasCalled;

    public TokenIntrospectionMiddlewareTests()
    {
        _next = context =>
        {
            _nextWasCalled = true;
            return Task.CompletedTask;
        };

        _logger = Substitute.For<ILogger<TokenIntrospectionMiddleware>>();
        _middleware = new TokenIntrospectionMiddleware(_next, _logger);
        _clientFactory = Substitute.For<IHttpClientFactory>();
        _cache = Substitute.For<IDistributedCache>();

        // Default options with introspection enabled
        _options = Options.Create(new ZeroTrustAuthOptions
        {
            Authority = "http://localhost:8080",
            Realm = "test",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            IntrospectionEnabled = true,
            IntrospectionCacheSeconds = 30
        });
    }

    [Fact]
    public async Task InvokeAsync_WhenIntrospectionDisabled_PassesThrough()
    {
        // Arrange
        var options = Options.Create(new ZeroTrustAuthOptions { IntrospectionEnabled = false });
        var context = CreateHttpContext();

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, options, _cache);

        // Assert
        _nextWasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenNoToken_PassesThrough()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: null);

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, _options, _cache);

        // Assert
        _nextWasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenTokenCachedAsActive_PassesThroughWithoutIntrospection()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: "valid-token");

        // Mock GetAsync (not GetStringAsync as that's an extension method)
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("active"));

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, _options, _cache);

        // Assert
        _nextWasCalled.ShouldBeTrue();
        // Should not have called the HTTP client since cache hit
        _clientFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_WhenTokenCachedAsRevoked_Rejects()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: "revoked-token");

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("revoked"));

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, _options, _cache);

        // Assert
        _nextWasCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task InvokeAsync_WhenIntrospectionReturnsActive_PassesThroughAndCaches()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: "active-token");

        // Cache miss
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var mockHttpClient = CreateMockHttpClient(new { active = true });
        _clientFactory.CreateClient("KeycloakIntrospection").Returns(mockHttpClient);

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, _options, _cache);

        // Assert
        _nextWasCalled.ShouldBeTrue();
        await _cache.Received().SetAsync(
            Arg.Any<string>(),
            Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b) == "active"),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_WhenIntrospectionReturnsInactive_RejectsAndCaches()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: "revoked-token");

        // Cache miss
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var mockHttpClient = CreateMockHttpClient(new { active = false });
        _clientFactory.CreateClient("KeycloakIntrospection").Returns(mockHttpClient);

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, _options, _cache);

        // Assert
        _nextWasCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(401);
        await _cache.Received().SetAsync(
            Arg.Any<string>(),
            Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b) == "revoked"),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_WhenIntrospectionEndpointUnavailable_FailsOpen()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: "some-token");

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "Service unavailable");
        var mockHttpClient = new HttpClient(mockHandler);
        _clientFactory.CreateClient("KeycloakIntrospection").Returns(mockHttpClient);

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, _options, _cache);

        // Assert
        // Fail-open behavior: allow request through when introspection is unavailable
        _nextWasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionDuringIntrospection_FailsOpen()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: "some-token");

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        _clientFactory.CreateClient("KeycloakIntrospection")
            .Returns(x => throw new HttpRequestException("Network error"));

        // Act
        await _middleware.InvokeAsync(context, _clientFactory, _options, _cache);

        // Assert
        // Fail-open on exception
        _nextWasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithNullCache_StillPerformsIntrospection()
    {
        // Arrange
        var context = CreateHttpContextWithServices(token: "valid-token");

        var mockHttpClient = CreateMockHttpClient(new { active = true });
        _clientFactory.CreateClient("KeycloakIntrospection").Returns(mockHttpClient);

        // Act - pass null cache
        await _middleware.InvokeAsync(context, _clientFactory, _options, null);

        // Assert
        _nextWasCalled.ShouldBeTrue();
    }

    /// <summary>
    /// Creates a basic HttpContext without services (for testing introspection disabled case)
    /// </summary>
    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>
    /// Creates an HttpContext with proper service mocking for token retrieval
    /// </summary>
    private static DefaultHttpContext CreateHttpContextWithServices(string? token)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Setup service provider with authentication service
        var authService = Substitute.For<IAuthenticationService>();

        if (token != null)
        {
            // Setup successful authentication with token
            var authProperties = new AuthenticationProperties();
            authProperties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);

            authService.AuthenticateAsync(context, Arg.Any<string?>())
                .Returns(AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(new ClaimsIdentity("Bearer")),
                        authProperties,
                        "Bearer")));
        }
        else
        {
            // No authentication
            authService.AuthenticateAsync(context, Arg.Any<string?>())
                .Returns(AuthenticateResult.NoResult());
        }

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        context.RequestServices = serviceProvider;

        return context;
    }

    private static HttpClient CreateMockHttpClient(object response)
    {
        var json = JsonSerializer.Serialize(response);
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        return new HttpClient(mockHandler);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }
}
