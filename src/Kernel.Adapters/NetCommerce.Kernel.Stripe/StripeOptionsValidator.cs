#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Kernel.Stripe;

/// <summary>
///     Fail-fast validation for <see cref="StripeOptions"/>.
///     A missing or placeholder secret key must stop the process at startup —
///     not at 3 AM when the first real payment attempt hits the Stripe API.
///     Checks apply to Production-like environments (Production + Staging) only,
///     so local development, tests, and design-time codegen are unaffected.
/// </summary>
public sealed partial class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<StripeOptionsValidator> _logger;

    public StripeOptionsValidator(
        IHostEnvironment environment,
        ILogger<StripeOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        if (string.IsNullOrWhiteSpace(options.SecretKey) || IsPlaceholder(options.SecretKey))
        {
            Log.SecretKeyMissing(_logger, _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "Stripe:SecretKey must be configured with a real key in Production/Staging.");
        }

        if (string.IsNullOrWhiteSpace(options.WebhookSecret) || IsPlaceholder(options.WebhookSecret))
        {
            Log.WebhookSecretMissing(_logger, _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "Stripe:WebhookSecret must be configured with a real value in Production/Staging.");
        }

        if (options.TestMode && _environment.IsProduction() && !options.AllowTestModeInProduction)
        {
            Log.TestModeInProduction(_logger);

            return ValidateOptionsResult.Fail(
                "Stripe:TestMode must be disabled in Production (or explicitly acknowledged via Stripe:AllowTestModeInProduction).");
        }

        Log.OptionsValidated(_logger, options.TestMode, options.TimeoutSeconds, options.MaxRetryAttempts);

        return ValidateOptionsResult.Success;
    }

    private static bool IsPlaceholder(string value)
    {
        return value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            || value.Contains("dummy", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Zero-allocation log messages (CA1848/CA1873): startup/validation paths use
    ///     LoggerMessage source generation instead of LoggerExtensions overloads.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Critical,
            Message = "FATAL: Stripe:SecretKey is missing or a placeholder in {Environment}. Payments cannot be processed. Set a real 'Stripe:SecretKey' before starting.")]
        public static partial void SecretKeyMissing(ILogger logger, string environment);

        [LoggerMessage(EventId = 2, Level = LogLevel.Critical,
            Message = "FATAL: Stripe:WebhookSecret is missing or a placeholder in {Environment}. Webhook signatures cannot be verified. Set a real 'Stripe:WebhookSecret' before starting.")]
        public static partial void WebhookSecretMissing(ILogger logger, string environment);

        [LoggerMessage(EventId = 3, Level = LogLevel.Critical,
            Message = "FATAL: Stripe:TestMode is enabled in Production. Test keys move no real money — orders would be fulfilled without payment. Set 'Stripe:TestMode=false' with live keys, or explicitly acknowledge with 'Stripe:AllowTestModeInProduction=true'.")]
        public static partial void TestModeInProduction(ILogger logger);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information,
            Message = "Stripe options validated: TestMode={TestMode}, Timeout={Timeout}s, MaxRetries={MaxRetries}")]
        public static partial void OptionsValidated(ILogger logger, bool testMode, int timeout, int maxRetries);
    }
}
