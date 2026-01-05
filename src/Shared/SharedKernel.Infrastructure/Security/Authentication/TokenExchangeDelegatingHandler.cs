#region

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

/// <summary>
///     RFC 8693 Token Exchange Delegating Handler.
///     Purpose: When NetCommerce.Api needs to call downstream services (e.g., Inventory, Payments),
///     it should NOT reuse the user's original token. This creates security risks:
///     - Audience mismatch (token was intended for API, not downstream service)
///     - Leaked token replay attacks
///     - Violation of principle of least privilege
///     Instead, this handler exchanges the user's token for a NEW token specifically
///     scoped to the target service (audience), maintaining the user's identity while
///     limiting the blast radius of a compromised token.
/// </summary>
public sealed class TokenExchangeDelegatingHandler : DelegatingHandler
{
    private readonly IDistributedCache? _cache;
    private readonly IHttpClientFactory _clientFactory;
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly ILogger<TokenExchangeDelegatingHandler> _logger;
    private readonly IOptions<ZeroTrustAuthOptions> _options;
    private readonly string _targetAudience;

    /// <summary>
    ///     Creates a new token exchange handler for a specific downstream service.
    /// </summary>
    /// <param name="contextAccessor">HTTP context accessor for retrieving the incoming token.</param>
    /// <param name="clientFactory">HTTP client factory for making exchange requests.</param>
    /// <param name="options">Authentication options containing Keycloak configuration.</param>
    /// <param name="cache">Optional distributed cache for caching exchanged tokens.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="targetAudience">The client ID of the downstream service (e.g., "inventory-service").</param>
    public TokenExchangeDelegatingHandler(
        IHttpContextAccessor contextAccessor,
        IHttpClientFactory clientFactory,
        IOptions<ZeroTrustAuthOptions> options,
        IDistributedCache? cache,
        ILogger<TokenExchangeDelegatingHandler> logger,
        string targetAudience)
    {
        _contextAccessor = contextAccessor;
        _clientFactory = clientFactory;
        _options = options;
        _cache = cache;
        _logger = logger;
        _targetAudience = targetAudience;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ZeroTrustAuthOptions authOptions = _options.Value;

        // Skip exchange if disabled
        if (!authOptions.TokenExchangeEnabled) return await base.SendAsync(request, cancellationToken);

        // Get incoming user token
        HttpContext? httpContext = _contextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning("No HttpContext available for token exchange");
            return await base.SendAsync(request, cancellationToken);
        }

        string? incomingToken = await httpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(incomingToken))
        {
            _logger.LogDebug("No access token available for exchange, proceeding without authorization");
            return await base.SendAsync(request, cancellationToken);
        }

        // Try to get cached exchanged token
        string cacheKey = $"token_exchange:{ComputeTokenHash(incomingToken)}:{_targetAudience}";
        string? exchangedToken = null;

        if (_cache is not null)
        {
            exchangedToken = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(exchangedToken))
                _logger.LogDebug("Using cached exchanged token for audience {Audience}", _targetAudience);
        }

        // Perform token exchange if not cached
        if (string.IsNullOrEmpty(exchangedToken))
        {
            TokenExchangeResult exchangeResult =
                await ExchangeTokenAsync(incomingToken, _targetAudience, cancellationToken);

            if (!exchangeResult.Success)
            {
                _logger.LogWarning(
                    "Token exchange failed for audience {Audience}: {Error}",
                    _targetAudience,
                    exchangeResult.Error);

                // Fall back to original token if exchange fails (configurable behavior)
                exchangedToken = incomingToken;
            }
            else
            {
                exchangedToken = exchangeResult.AccessToken!;

                // Cache the exchanged token
                if (_cache is not null && exchangeResult.ExpiresIn > 0)
                {
                    int cacheSeconds = Math.Max(exchangeResult.ExpiresIn - 30, 30);
                    var cacheOptions = new DistributedCacheEntryOptions
                    {
                        // Cache for slightly less than token lifetime
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cacheSeconds)
                    };
                    await _cache.SetStringAsync(cacheKey, exchangedToken, cacheOptions, cancellationToken);
                }

                _logger.LogDebug(
                    "Token exchanged successfully for audience {Audience}, expires in {ExpiresIn}s",
                    _targetAudience,
                    exchangeResult.ExpiresIn);
            }
        }

        // Attach the exchanged token to the downstream request
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", exchangedToken);

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<TokenExchangeResult> ExchangeTokenAsync(
        string subjectToken,
        string targetAudience,
        CancellationToken cancellationToken)
    {
        ZeroTrustAuthOptions authOptions = _options.Value;

        if (string.IsNullOrEmpty(authOptions.TokenEndpoint))
            return TokenExchangeResult.Failed("Token endpoint not configured");

        try
        {
            HttpClient client = _clientFactory.CreateClient("KeycloakTokenExchange");

            var request = new HttpRequestMessage(HttpMethod.Post, authOptions.TokenEndpoint);

            // RFC 8693 Token Exchange request
            var data = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["client_id"] = authOptions.ClientId,
                ["client_secret"] = authOptions.ClientSecret,
                ["subject_token"] = subjectToken,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["audience"] = targetAudience
            };

            request.Content = new FormUrlEncodedContent(data);

            HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return TokenExchangeResult.Failed($"HTTP {response.StatusCode}: {errorContent}");
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDoc = JsonDocument.Parse(content);
            JsonElement json = jsonDoc.RootElement;

            if (!json.TryGetProperty("access_token", out JsonElement accessTokenElement))
                return TokenExchangeResult.Failed("Response missing access_token");

            string accessToken = accessTokenElement.GetString()!;
            int expiresIn = json.TryGetProperty("expires_in", out JsonElement expiresInElement)
                ? expiresInElement.GetInt32()
                : 300; // Default 5 minutes

            return TokenExchangeResult.Succeeded(accessToken, expiresIn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token exchange for audience {Audience}", targetAudience);
            return TokenExchangeResult.Failed($"Exception: {ex.Message}");
        }
    }

    private static string ComputeTokenHash(string token)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash)[..16];
    }

    private readonly record struct TokenExchangeResult(
        bool Success,
        string? AccessToken,
        int ExpiresIn,
        string? Error)
    {
        public static TokenExchangeResult Succeeded(string accessToken, int expiresIn)
        {
            return new TokenExchangeResult(true, accessToken, expiresIn, null);
        }

        public static TokenExchangeResult Failed(string error)
        {
            return new TokenExchangeResult(false, null, 0, error);
        }
    }
}

/// <summary>
///     Factory for creating TokenExchangeDelegatingHandler instances for specific downstream services.
/// </summary>
public sealed class TokenExchangeHandlerFactory
{
    private readonly IDistributedCache? _cache;
    private readonly IHttpClientFactory _clientFactory;
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<ZeroTrustAuthOptions> _options;

    public TokenExchangeHandlerFactory(
        IHttpContextAccessor contextAccessor,
        IHttpClientFactory clientFactory,
        IOptions<ZeroTrustAuthOptions> options,
        ILoggerFactory loggerFactory,
        IDistributedCache? cache = null)
    {
        _contextAccessor = contextAccessor;
        _clientFactory = clientFactory;
        _options = options;
        _cache = cache;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    ///     Creates a handler for exchanging tokens to the specified downstream service.
    /// </summary>
    /// <param name="targetAudience">The client ID of the downstream service.</param>
    public TokenExchangeDelegatingHandler CreateHandler(string targetAudience)
    {
        return new TokenExchangeDelegatingHandler(
            _contextAccessor,
            _clientFactory,
            _options,
            _cache,
            _loggerFactory.CreateLogger<TokenExchangeDelegatingHandler>(),
            targetAudience);
    }
}
