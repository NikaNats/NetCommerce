#nullable enable
namespace NetCommerce.Kernel.Security.Authentication;

/// <summary>
///     Zero-Trust Authentication configuration options.
///     Extends basic OIDC options with introspection and token exchange settings.
///     This is the canonical source - replaces SharedKernel.Infrastructure version.
/// </summary>
public sealed class ZeroTrustAuthOptions
{
    /// <summary>
    ///     Configuration section name for binding.
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    ///     Gets or sets the OIDC authority URL (e.g., Keycloak realm endpoint).
    ///     Set by Aspire as Keycloak__AuthServerUrl.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the OIDC realm name (Keycloak-specific).
    ///     Set by Aspire as Keycloak__Realm.
    /// </summary>
    public string Realm { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the expected audience (client ID of the API).
    ///     Override via Auth__Audience environment variable.
    /// </summary>
    public string Audience { get; set; } = "netcommerce-api";

    /// <summary>
    ///     Gets or sets the API scope required for access.
    ///     Override via Auth__ApiScope environment variable.
    /// </summary>
    public string ApiScope { get; set; } = "netcommerce.api";

    /// <summary>
    ///     Gets or sets the client ID for service-to-service authentication.
    ///     Used for token introspection and token exchange.
    /// </summary>
    public string ClientId { get; set; } = "netcommerce-api";

    /// <summary>
    ///     Gets or sets the client secret for service-to-service authentication.
    ///     In production, retrieve from Azure Key Vault or similar.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets whether token introspection (kill switch) is enabled.
    ///     When true, every request validates the token against the identity provider.
    ///     Default: false (enable in production for 2025+ security standards).
    /// </summary>
    public bool IntrospectionEnabled { get; set; } = false;

    /// <summary>
    ///     Gets or sets the introspection cache duration in seconds.
    ///     Caches introspection results to reduce identity provider load.
    ///     Default: 30 seconds (balance between security and performance).
    ///     Zero-Trust standard: Max 30s for instant ban propagation.
    /// </summary>
    public int IntrospectionCacheSeconds { get; set; } = 30;

    /// <summary>
    ///     Gets or sets whether token exchange is enabled for downstream services.
    ///     Implements RFC 8693 for secure service-to-service communication.
    /// </summary>
    public bool TokenExchangeEnabled { get; set; } = true;

    /// <summary>
    ///     Gets the full realm URL for token endpoints (Keycloak-specific).
    /// </summary>
    public string RealmUrl => string.IsNullOrEmpty(Authority) || string.IsNullOrEmpty(Realm)
        ? string.Empty
        : $"{Authority.TrimEnd('/')}/realms/{Realm}";

    /// <summary>
    ///     Gets the token endpoint URL.
    /// </summary>
    public string TokenEndpoint => string.IsNullOrEmpty(RealmUrl)
        ? string.Empty
        : $"{RealmUrl}/protocol/openid-connect/token";

    /// <summary>
    ///     Gets the introspection endpoint URL (RFC 7662).
    /// </summary>
    public string IntrospectionEndpoint => string.IsNullOrEmpty(RealmUrl)
        ? string.Empty
        : $"{RealmUrl}/protocol/openid-connect/token/introspect";
}
