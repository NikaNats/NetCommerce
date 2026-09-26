#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Ordering.Infrastructure.Notifications;

/// <summary>
///     Fail-fast validation for <see cref="EmailProviderOptions"/>.
///     Order confirmations, financial alerts, and dispute notices all flow
///     through <c>IEmailProvider</c> — an InMemory provider in Production
///     silently drops every one of them.
/// </summary>
public sealed class EmailProviderOptionsValidator : IValidateOptions<EmailProviderOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<EmailProviderOptionsValidator> _logger;

    public EmailProviderOptionsValidator(
        IHostEnvironment environment,
        ILogger<EmailProviderOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, EmailProviderOptions options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        if (string.Equals(options.Provider, "SendGrid", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.SendGridApiKey))
                return ValidateOptionsResult.Fail("Ordering:Email:SendGridApiKey must be configured when Provider=SendGrid.");

            if (string.IsNullOrWhiteSpace(options.FromEmail))
                return ValidateOptionsResult.Fail("Ordering:Email:FromEmail (verified sender) must be configured when Provider=SendGrid.");

            return ValidateOptionsResult.Success;
        }

        if (string.Equals(options.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            if (!options.AllowInMemoryInProduction)
            {
                _logger.LogCritical(
                    "FATAL: Ordering:Email:Provider is InMemory in {Environment} without acknowledgment. " +
                    "Order confirmation and alert emails would be silently swallowed. Configure a real provider " +
                    "(Ordering:Email:Provider=SendGrid with SendGridApiKey/FromEmail) or explicitly acknowledge " +
                    "with 'Ordering:Email:AllowInMemoryInProduction=true'.",
                    _environment.EnvironmentName);

                return ValidateOptionsResult.Fail(
                    "Ordering:Email:Provider=InMemory is not allowed in Production/Staging without explicit acknowledgment.");
            }

            _logger.LogWarning(
                "Ordering:Email:Provider is InMemory in {Environment} by explicit acknowledgment. Emails are swallowed.",
                _environment.EnvironmentName);

            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail($"Ordering:Email:Provider '{options.Provider}' is unknown. Supported: InMemory, SendGrid.");
    }
}
