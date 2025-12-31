using System.ComponentModel.DataAnnotations;

namespace NetCommerce.Api.Authentication;

/// <summary>
/// Configuration options for Keycloak authentication settings.
/// </summary>
public class AuthOptions
{
    /// <summary>
    /// The configuration section name for authentication options.
    /// Maps to Keycloak__ environment variables injected by Aspire.
    /// </summary>
    public const string SectionName = "Keycloak";

    /// <summary>
    /// Gets or sets the Keycloak server URL (set by Aspire as Keycloak__AuthServerUrl).
    /// </summary>
    public string AuthServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Keycloak realm name (set by Aspire as Keycloak__Realm).
    /// </summary>
    public string Realm { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected audience (client ID of the API).
    /// Override via Auth__Audience environment variable.
    /// </summary>
    public string Audience { get; set; } = "netcommerce-api";

    /// <summary>
    /// Gets or sets the API scope required for access.
    /// Override via Auth__ApiScope environment variable.
    /// </summary>
    public string ApiScope { get; set; } = "netcommerce.api";

    /// <summary>
    /// Gets the full authority URL (realm endpoint) for JWT validation.
    /// </summary>
    public string Authority => $"{AuthServerUrl.TrimEnd('/')}/realms/{Realm}";
}
