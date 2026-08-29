#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Kernel.Security.Authorization;

/// <summary>
///     Validates <see cref="AdminElevatedAuthOptions"/> at application startup.
/// </summary>
public sealed class AdminElevatedAuthOptionsValidator : IValidateOptions<AdminElevatedAuthOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AdminElevatedAuthOptionsValidator> _logger;

    public AdminElevatedAuthOptionsValidator(
        IHostEnvironment environment,
        ILogger<AdminElevatedAuthOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, AdminElevatedAuthOptions options)
    {
        var isProductionLike =
            _environment.IsProduction() ||
            _environment.IsStaging();

        // ── RULE 1: API key must be configured in production-like environments ──
        if (!options.IsApiKeyConfigured)
        {
            if (isProductionLike)
            {
                _logger.LogCritical(
                    "FATAL: Auth:AdminElevated:ApiKey is not configured or too short (minimum 32 characters). Elevated admin endpoints (DLQ replay, force-complete saga, override payment) will be INACCESSIBLE. Set the 'Auth:AdminElevated:ApiKey' environment variable before starting.");

                return ValidateOptionsResult.Fail(
                    "Auth:AdminElevated:ApiKey must be configured (≥ 32 chars) in Production/Staging. Elevated admin operations are denied without a configured key to prevent silent security bypass.");
            }

            _logger.LogWarning(
                "Auth:AdminElevated:ApiKey is not configured. Elevated admin endpoints will deny ALL requests. Set the key before testing admin operations.");
        }

        // ── RULE 2: DevelopmentOnly mode is forbidden outside Development ──
        if (options.SecurityMode == AdminElevatedSecurityMode.DevelopmentOnly
            && !_environment.IsDevelopment())
        {
            _logger.LogCritical(
                "FATAL: Auth:AdminElevated:SecurityMode is set to 'DevelopmentOnly' but the environment is '{Environment}'. This mode is restricted to Development only.", _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "SecurityMode 'DevelopmentOnly' is only permitted in the Development environment.");
        }

        // ── RULE 3: Log the effective security posture ──
        _logger.LogInformation(
            "Admin elevated auth configured: Mode={SecurityMode}, ApiKeyConfigured={KeyConfigured}, MaxAuthAge={MaxAge}min",
            options.SecurityMode,
            options.IsApiKeyConfigured,
            options.MaxAuthAgeMinutes);

        return ValidateOptionsResult.Success;
    }
}
