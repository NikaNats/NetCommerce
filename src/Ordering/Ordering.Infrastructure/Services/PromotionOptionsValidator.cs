#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Fail-fast validation for <see cref="PromotionOptions"/>.
///     Coupons discount real orders; running Production-like environments on an
///     unreviewed coupon table (e.g. a forgotten 25% FIRSTORDER) leaks revenue.
/// </summary>
public sealed class PromotionOptionsValidator : IValidateOptions<PromotionOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PromotionOptionsValidator> _logger;

    public PromotionOptionsValidator(
        IHostEnvironment environment,
        ILogger<PromotionOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, PromotionOptions options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        if (!options.AcknowledgedInProduction)
        {
            _logger.LogCritical(
                "FATAL: Ordering:Promotions coupon table has not been reviewed for {Environment}. " +
                "Have marketing/finance verify 'Ordering:Promotions:Coupons', then set " +
                "'Ordering:Promotions:AcknowledgedInProduction=true'.",
                _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "Ordering:Promotions:AcknowledgedInProduction must be set after review in Production/Staging.");
        }

        foreach (var (code, fraction) in options.Coupons)
        {
            if (fraction is <= 0 or >= 1)
            {
                return ValidateOptionsResult.Fail(
                    $"Ordering:Promotions:Coupons:{code} must be a fraction in (0, 1).");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
