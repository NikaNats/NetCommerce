using Microsoft.Extensions.Diagnostics.HealthChecks;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.Security.Authentication;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence;

namespace NetCommerce.Api.Extensions.Hosting;

/// <summary>
///     Dependency health checks for the readiness probe.
///     The shared AddDefaultHealthChecks registers only "self", so /health/ready
///     reported healthy with Postgres, Redis and Keycloak all down — while the
///     orchestrator gated traffic on it. Each check below names the dependency
///     it guards; /health/live stays dependency-free by design (see
///     ServiceDefaults MapDefaultEndpoints).
/// </summary>
public static class ApiHealthChecksExtensions
{
    public static IServiceCollection AddApiHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services.AddHealthChecks()
            // One check per bounded-context database: a missing migration or a
            // down database must fail readiness, not first-request latency.
            .AddDbContextCheck<CatalogDbContext>("catalog-db")
            .AddDbContextCheck<OrderingDbContext>("ordering-db")
            .AddDbContextCheck<InventoryDbContext>("inventory-db")
            .AddDbContextCheck<PaymentsDbContext>("payments-db")
            .AddDbContextCheck<FinanceDbContext>("finance-db")
            .AddDbContextCheck<ShippingDbContext>("shipping-db");

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            builder.AddRedis(redisConnectionString, name: "redis");
        }

        services.AddTransient<KeycloakDiscoveryHealthCheck>();
        builder.AddCheck<KeycloakDiscoveryHealthCheck>("keycloak");

        return services;
    }
}

/// <summary>
///     Keycloak OIDC discovery probe.
///     An unreachable authority means no logins, no token refreshes and no
///     introspection — the API cannot do useful work, so this is Unhealthy
///     (not Degraded). Unconfigured authority is Degraded: auth fails closed
///     elsewhere at startup, but the probe reports what it sees.
/// </summary>
public sealed class KeycloakDiscoveryHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public KeycloakDiscoveryHealthCheck(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Same resolution as ZeroTrustAuthenticationExtensions: Keycloak section
        // first (Aspire/prod convention), Auth section as the direct override.
        // Reading only Auth:Authority would report Degraded in prod while auth
        // itself works fine via Keycloak__AuthServerUrl.
        var keycloakSection = _configuration.GetSection("Keycloak");
        var authSection = _configuration.GetSection(ZeroTrustAuthOptions.SectionName);
        var authority = keycloakSection.GetValue<string>("AuthServerUrl")
            ?? authSection.GetValue<string>(nameof(ZeroTrustAuthOptions.Authority));
        var realm = keycloakSection.GetValue<string>("Realm")
            ?? authSection.GetValue<string>(nameof(ZeroTrustAuthOptions.Realm));
        var realmUrl = string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(realm)
            ? string.Empty
            : $"{authority.TrimEnd('/')}/realms/{realm}";

        if (string.IsNullOrEmpty(realmUrl))
        {
            return HealthCheckResult.Degraded("Keycloak authority/realm is not configured.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = Timeout;
            var response = await client.GetAsync(
                $"{realmUrl}/.well-known/openid-configuration",
                timeout.Token);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Discovery answered {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Keycloak authority unreachable.", ex);
        }
    }
}
