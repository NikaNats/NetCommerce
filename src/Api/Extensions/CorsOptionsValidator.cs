using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Api.Extensions;

/// <summary>
///     The CORS allowlist as bound configuration.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>
///     Fail-fast validation for <see cref="CorsOptions"/>.
///     An unset/empty allowlist previously fell back to localhost origins, which
///     in production either blocks the real storefront (visible breakage) or
///     tempts an AllowAnyOrigin "fix" (fail-open). So every non-dev environment
///     must declare its storefront origin(s) explicitly.
///     Uses the REAL host environment (injected): reading the environment name
///     back out of configuration misdetects WebApplicationFactory hosts, which
///     set the environment via UseEnvironment without setting the env var.
///     Checks apply to Production/Staging only, so local development and the
///     test suite keep the localhost convenience defaults.
/// </summary>
public sealed partial class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CorsOptionsValidator> _logger;

    public CorsOptionsValidator(
        IHostEnvironment environment,
        ILogger<CorsOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        var origins = (options.AllowedOrigins ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .ToArray();

        if (origins.Length == 0)
        {
            Log.AllowlistMissing(_logger, _environment.EnvironmentName);
            return ValidateOptionsResult.Fail(
                "Cors:AllowedOrigins must list the storefront origin(s) in Production/Staging. " +
                "Unset means unknown, and unknown must not become localhost-with-credentials.");
        }

        return ValidateOptionsResult.Success;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Critical,
            Message = "FATAL: Cors:AllowedOrigins is empty in {Environment}. Set it to the storefront origin(s) before starting.")]
        public static partial void AllowlistMissing(ILogger logger, string environment);
    }
}
