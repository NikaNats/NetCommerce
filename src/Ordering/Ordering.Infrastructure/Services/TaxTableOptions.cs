#nullable enable
namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Configurable tax tables backing <see cref="LocalTaxProvider"/>.
///     Bind from the <c>Ordering:Tax</c> section (e.g. <c>Ordering__Tax__Rates__GE=0.18</c>).
///     Rates wrong here become wrong charges everywhere, so Production-like
///     environments must explicitly acknowledge the table (see validator).
/// </summary>
public sealed class TaxTableOptions
{
    public const string SectionName = "Ordering:Tax";

    /// <summary>
    ///     Base tax rate per ISO country code (0.18 = 18%).
    /// </summary>
    public Dictionary<string, decimal> Rates { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GE"] = 0.18m,
        ["US"] = 0.07m,
        ["GB"] = 0.20m,
        ["DE"] = 0.19m,
        ["FR"] = 0.20m,
        ["EU"] = 0.20m,
        ["CA"] = 0.13m,
        ["AU"] = 0.10m,
        ["IN"] = 0.18m
    };

    /// <summary>
    ///     Category multiplier applied to the base rate (0.5 = half rate, 0 = exempt).
    /// </summary>
    public Dictionary<string, decimal> CategoryAdjustments { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FOOD"] = 0.5m,
        ["BOOKS"] = 0.5m,
        ["CHILDREN"] = 0.5m,
        ["MEDICAL"] = 0m,
        ["EDUCATION"] = 0m
    };

    /// <summary>
    ///     Explicit acknowledgment that the configured (or default) rate tables
    ///     have been reviewed by finance/tax for Production use.
    /// </summary>
    public bool AcknowledgedInProduction { get; set; } = false;
}
