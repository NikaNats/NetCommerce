#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Kernel.Security.Authentication;

/// <summary>
///     Fail-fast validation for <see cref="ZeroTrustAuthOptions"/>.
///     An empty Authority/Realm produces an empty JWT authority, which fails
///     every authenticated request with obscure errors. Stop at startup instead.
///     Applies to Production-like environments (Production + Staging) only.
/// </summary>
public sealed class ZeroTrustAuthOptionsValidator : IValidateOptions<ZeroTrustAuthOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ZeroTrustAuthOptionsValidator> _logger;

    public ZeroTrustAuthOptionsValidator(
        IHostEnvironment environment,
        ILogger<ZeroTrustAuthOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, ZeroTrustAuthOptions options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        if (string.IsNullOrWhiteSpace(options.Authority) || string.IsNullOrWhiteSpace(options.Realm))
        {
            _logger.LogCritical(
                "FATAL: OIDC authority/realm is not configured in {Environment} " +
                "(Auth section / Keycloak__AuthServerUrl / Keycloak__Realm). " +
                "JWT validation cannot work. Configure the identity provider before starting.",
                _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "OIDC Authority and Realm must be configured in Production/Staging.");
        }

        if (options.IntrospectionEnabled && string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            _logger.LogCritical(
                "FATAL: Token introspection is enabled but no client secret is configured in {Environment}.",
                _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "A client secret is required when token introspection is enabled.");
        }

        if (options.TokenExchangeEnabled && string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            _logger.LogWarning(
                "Token exchange is enabled but no client secret is configured. " +
                "Downstream token exchange calls will fail at runtime.");
        }

        _logger.LogInformation(
            "Zero-trust auth validated: RealmUrl={RealmUrl}, Introspection={Introspection}",
            options.RealmUrl,
            options.IntrospectionEnabled);

        return ValidateOptionsResult.Success;
    }
}
