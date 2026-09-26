#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Fail-fast validation for <see cref="TaxTableOptions"/>.
///     Every order total depends on these rates; running Production-like
///     environments on unreviewed tables is silent systematic mis-taxation.
/// </summary>
public sealed class TaxTableOptionsValidator : IValidateOptions<TaxTableOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<TaxTableOptionsValidator> _logger;

    public TaxTableOptionsValidator(
        IHostEnvironment environment,
        ILogger<TaxTableOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, TaxTableOptions options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        if (!options.AcknowledgedInProduction)
        {
            _logger.LogCritical(
                "FATAL: Ordering:Tax rate tables have not been reviewed for {Environment}. " +
                "Have finance/tax verify 'Ordering:Tax:Rates' (and CategoryAdjustments), then set " +
                "'Ordering:Tax:AcknowledgedInProduction=true'.",
                _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "Ordering:Tax:AcknowledgedInProduction must be set after finance review in Production/Staging.");
        }

        if (options.Rates.Count == 0)
        {
            _logger.LogCritical("FATAL: Ordering:Tax:Rates is empty — no country can be taxed.");
            return ValidateOptionsResult.Fail("Ordering:Tax:Rates must not be empty.");
        }

        return ValidateOptionsResult.Success;
    }
}
