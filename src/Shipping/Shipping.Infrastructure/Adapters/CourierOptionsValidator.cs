#nullable enable
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetCommerce.Shipping.Infrastructure.Adapters;

/// <summary>
///     Fail-fast validation for <see cref="CourierOptions"/>.
///     Mock couriers return FAKE tracking numbers and label URLs — serving them
///     to real customers is a fulfillment outage by another name. Booting a
///     Production-like environment in mock mode requires explicit acknowledgment.
/// </summary>
public sealed class CourierOptionsValidator : IValidateOptions<CourierOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CourierOptionsValidator> _logger;

    public CourierOptionsValidator(
        IHostEnvironment environment,
        ILogger<CourierOptionsValidator> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, CourierOptions options)
    {
        if (!_environment.IsProduction() && !_environment.IsStaging())
            return ValidateOptionsResult.Success;

        if (options.UseMockMode && !options.AllowMockInProduction)
        {
            _logger.LogCritical(
                "FATAL: Couriers:UseMockMode is enabled in {Environment} without acknowledgment. " +
                "Mock labels carry fake tracking numbers. Configure real DHL/FedEx credentials " +
                "(Couriers:UseMockMode=false) or explicitly acknowledge with 'Couriers:AllowMockInProduction=true'.",
                _environment.EnvironmentName);

            return ValidateOptionsResult.Fail(
                "Couriers:UseMockMode must be disabled in Production/Staging (or explicitly acknowledged via Couriers:AllowMockInProduction).");
        }

        if (!options.UseMockMode)
        {
            if (string.IsNullOrWhiteSpace(options.Dhl.ApiKey) && string.IsNullOrWhiteSpace(options.FedEx.ClientId))
            {
                _logger.LogCritical(
                    "FATAL: Couriers:UseMockMode is disabled in {Environment} but neither DHL nor FedEx " +
                    "credentials are configured. Label creation will fail for every shipment.",
                    _environment.EnvironmentName);

                return ValidateOptionsResult.Fail(
                    "Real courier mode requires DHL (Couriers:Dhl:ApiKey) or FedEx (Couriers:FedEx:ClientId) credentials.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
