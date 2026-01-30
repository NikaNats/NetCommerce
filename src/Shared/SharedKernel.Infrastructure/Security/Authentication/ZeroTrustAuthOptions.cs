// =============================================================================
// DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthOptions
// This file exists for backward compatibility only.
// =============================================================================
using NewZeroTrustAuthOptions = NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthOptions;

namespace NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

/// <summary>
///     DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthOptions instead.
///     This class forwards to the canonical implementation in Kernel.Security.
/// </summary>
[Obsolete("Use NetCommerce.Kernel.Security.Authentication.ZeroTrustAuthOptions instead.")]
public sealed class ZeroTrustAuthOptions
{
    public const string SectionName = NewZeroTrustAuthOptions.SectionName;

    public string Authority { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string Audience { get; set; } = "netcommerce-api";
    public string ApiScope { get; set; } = "netcommerce.api";
    public string ClientId { get; set; } = "netcommerce-api";
    public string ClientSecret { get; set; } = string.Empty;
    public bool IntrospectionEnabled { get; set; } = false;
    public int IntrospectionCacheSeconds { get; set; } = 30;
    public bool TokenExchangeEnabled { get; set; } = true;

    public string RealmUrl => string.IsNullOrEmpty(Authority) || string.IsNullOrEmpty(Realm)
        ? string.Empty
        : $"{Authority.TrimEnd('/')}/realms/{Realm}";

    public string TokenEndpoint => string.IsNullOrEmpty(RealmUrl)
        ? string.Empty
        : $"{RealmUrl}/protocol/openid-connect/token";

    public string IntrospectionEndpoint => string.IsNullOrEmpty(RealmUrl)
        ? string.Empty
        : $"{RealmUrl}/protocol/openid-connect/token/introspect";
}
