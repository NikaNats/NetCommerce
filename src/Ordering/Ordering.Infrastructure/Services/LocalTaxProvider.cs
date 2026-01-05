#nullable enable

using NetCommerce.Ordering.Domain.Orders;

namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Local fallback tax provider implementing simple jurisdiction-based rules.
///     Used when external tax services are unavailable or as a default implementation.
///     This ensures the checkout flow never breaks due to tax API downtime.
/// </summary>
public sealed class LocalTaxProvider : ITaxProvider
{
    // Simple tax rate table - in production, this might be loaded from configuration
    private readonly Dictionary<string, decimal> _taxRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GE"] = 0.18m, // Georgia VAT: 18%
        ["US"] = 0.07m, // US average sales tax: ~7%
        ["GB"] = 0.20m, // UK VAT: 20%
        ["DE"] = 0.19m, // Germany VAT: 19%
        ["FR"] = 0.20m, // France VAT: 20%
        ["EU"] = 0.20m, // EU default VAT: 20%
        ["CA"] = 0.13m, // Canada HST/GST average: ~13%
        ["AU"] = 0.10m, // Australia GST: 10%
        ["IN"] = 0.18m, // India GST: 18%
    };

    // Category-specific adjustments (e.g., reduced rates for food, books)
    private readonly Dictionary<string, decimal> _categoryAdjustments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FOOD"] = 0.5m,        // 50% reduction (e.g., 18% -> 9%)
        ["BOOKS"] = 0.5m,       // 50% reduction
        ["CHILDREN"] = 0.5m,    // 50% reduction for children's items
        ["MEDICAL"] = 0m,       // Tax-exempt
        ["EDUCATION"] = 0m      // Tax-exempt
    };

    public Task<TaxCalculationResult> GetTaxAsync(
        decimal amount,
        string countryCode,
        string? category,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            return Task.FromResult(TaxCalculationResult.NoTax("LocalTaxProvider"));

        if (string.IsNullOrWhiteSpace(countryCode))
            countryCode = "GE"; // Default to Georgia

        // Get base tax rate for country
        if (!_taxRates.TryGetValue(countryCode.ToUpperInvariant(), out var baseRate))
        {
            // Default to Georgia rate if country not found
            baseRate = _taxRates["GE"];
        }

        // Apply category adjustment if applicable
        var adjustedRate = baseRate;
        if (!string.IsNullOrWhiteSpace(category) && 
            _categoryAdjustments.TryGetValue(category, out var adjustment))
        {
            adjustedRate *= adjustment;
        }

        var taxAmount = Math.Round(amount * adjustedRate, 2);

        var taxType = DetermineTaxType(countryCode);
        var result = new TaxCalculationResult(
            taxAmount,
            adjustedRate,
            taxType,
            "LocalTaxProvider");

        return Task.FromResult(result);
    }

    private static string DetermineTaxType(string countryCode)
    {
        return countryCode.ToUpperInvariant() switch
        {
            "US" or "CA" => "SALES_TAX",
            "AU" or "IN" => "GST",
            _ => "VAT" // Most countries use VAT
        };
    }
}
