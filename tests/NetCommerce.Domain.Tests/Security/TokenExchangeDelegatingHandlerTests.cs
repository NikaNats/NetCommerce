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
///     Unit tests for TokenExchangeDelegatingHandler.
///     Verifies that token exchange correctly obtains scoped tokens for downstream services.
/// </summary>
public class TokenExchangeDelegatingHandlerTests
{
    private const string IncomingToken = "incoming-user-token";
    private const string ExchangedToken = "exchanged-service-token";
    private const string TargetAudience = "inventory-service";

    private readonly IHttpContextAccessor _contextAccessor;
    private readonly IHttpClientFactory _clientFactory;
    private readonly IOptions<ZeroTrustAuthOptions> _options;
    private readonly IDistributedCache _cache;
    private readonly ILogger<TokenExchangeDelegatingHandler> _logger;

    public TokenExchangeDelegatingHandlerTests()
    {
        _contextAccessor = Substitute.For<IHttpContextAccessor>();
        _clientFactory = Substitute.For<IHttpClientFactory>();
        _cache = Substitute.For<IDistributedCache>();
        _logger = Substitute.For<ILogger<TokenExchangeDelegatingHandler>>();

        _options = Options.Create(new ZeroTrustAuthOptions
        {
            Authority = "http://localhost:8080",
            Realm = "test",
            ClientId = "netcommerce-api",
            ClientSecret = "test-secret",
            TokenExchangeEnabled = true
        });
    }

    [Fact]
    public async Task SendAsync_WhenTokenExchangeDisabled_DoesNotExchange()
    {
        // Arrange
        var options = Options.Create(new ZeroTrustAuthOptions { TokenExchangeEnabled = false });
        var handler = CreateHandler(options);
        var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");
        SetupHttpContext(IncomingToken);

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        request.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_WhenNoHttpContext_DoesNotExchange()
    {
        // Arrange
        var handler = CreateHandler();
        var invoker = new HttpMessageInvoker(handler);

        _contextAccessor.HttpContext.Returns((HttpContext?)null);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        request.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_WhenNoAccessToken_DoesNotExchange()
    {
        // Arrange
        var handler = CreateHandler();
        var invoker = new HttpMessageInvoker(handler);
        SetupHttpContext(token: null);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        request.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_WhenCachedTokenExists_UsesCachedToken()
    {
        // Arrange
        var handler = CreateHandler();
        var invoker = new HttpMessageInvoker(handler);
        SetupHttpContext(IncomingToken);

        // Use GetAsync (byte[]) not GetStringAsync - extension methods can't be mocked
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("cached-exchanged-token"));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("cached-exchanged-token");

        // Should not have called Keycloak for exchange
        _clientFactory.DidNotReceive().CreateClient("KeycloakTokenExchange");
    }

    [Fact]
    public async Task SendAsync_WhenExchangeSucceeds_AttachesExchangedToken()
    {
        // Arrange
        var handler = CreateHandler();
        var invoker = new HttpMessageInvoker(handler);
        SetupHttpContext(IncomingToken);

        // Cache miss
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        SetupSuccessfulTokenExchange(ExchangedToken, expiresIn: 300);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe(ExchangedToken);
    }

    [Fact]
    public async Task SendAsync_WhenExchangeSucceeds_CachesToken()
    {
        // Arrange
        var handler = CreateHandler();
        var invoker = new HttpMessageInvoker(handler);
        SetupHttpContext(IncomingToken);

        // Cache miss
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        SetupSuccessfulTokenExchange(ExchangedToken, expiresIn: 300);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");

        // Act
        await invoker.SendAsync(request, CancellationToken.None);

        // Assert - Use SetAsync not SetStringAsync
        await _cache.Received().SetAsync(
            Arg.Any<string>(),
            Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b) == ExchangedToken),
            Arg.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow!.Value.TotalSeconds < 300),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WhenExchangeFails_FallsBackToOriginalToken()
    {
        // Arrange
        var handler = CreateHandler();
        var invoker = new HttpMessageInvoker(handler);
        SetupHttpContext(IncomingToken);

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        SetupFailedTokenExchange();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");

        // Act
        var response = await invoker.SendAsync(request, CancellationToken.None);

        // Assert - falls back to original token
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization!.Parameter.ShouldBe(IncomingToken);
    }

    [Fact]
    public async Task SendAsync_WithNullCache_StillPerformsExchange()
    {
        // Arrange
        var handler = new TokenExchangeDelegatingHandler(
            _contextAccessor,
            _clientFactory,
            _options,
            null, // No cache
            _logger,
            TargetAudience)
        {
            InnerHandler = new MockInnerHandler()
        };

        SetupHttpContext(IncomingToken);
        SetupSuccessfulTokenExchange(ExchangedToken, 300);

        var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://inventory-service/api/stock");

        // Act
        await invoker.SendAsync(request, CancellationToken.None);

        // Assert
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization!.Parameter.ShouldBe(ExchangedToken);
    }

    private TokenExchangeDelegatingHandler CreateHandler(IOptions<ZeroTrustAuthOptions>? options = null)
    {
        var handler = new TokenExchangeDelegatingHandler(
            _contextAccessor,
            _clientFactory,
            options ?? _options,
            _cache,
            _logger,
            TargetAudience)
        {
            InnerHandler = new MockInnerHandler()
        };

        return handler;
    }

    private void SetupHttpContext(string? token)
    {
        var context = new DefaultHttpContext();

        // Setup service provider with authentication service
        var authService = Substitute.For<IAuthenticationService>();

        if (token != null)
        {
            // Setup successful authentication with token stored properly
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

        _contextAccessor.HttpContext.Returns(context);
    }

    private void SetupSuccessfulTokenExchange(string accessToken, int expiresIn)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = expiresIn
        });

        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        var mockHttpClient = new HttpClient(mockHandler);
        _clientFactory.CreateClient("KeycloakTokenExchange").Returns(mockHttpClient);
    }

    private void SetupFailedTokenExchange()
    {
        var mockHandler = new MockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new { error = "invalid_grant" }));
        var mockHttpClient = new HttpClient(mockHandler);
        _clientFactory.CreateClient("KeycloakTokenExchange").Returns(mockHttpClient);
    }

    private class MockInnerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
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
