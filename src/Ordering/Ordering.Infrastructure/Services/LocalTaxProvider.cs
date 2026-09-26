#region

using NetCommerce.Ordering.Domain.Orders;

#endregion

namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Local fallback tax provider implementing simple jurisdiction-based rules.
///     Used when external tax services are unavailable or as a default implementation.
///     This ensures the checkout flow never breaks due to tax API downtime.
///     Rate tables come from <see cref="TaxTableOptions"/> (config-overridable);
///     Production-like environments must acknowledge them (see validator).
/// </summary>
public sealed class LocalTaxProvider : ITaxProvider
{
    private readonly Dictionary<string, decimal> _categoryAdjustments;
    private readonly Dictionary<string, decimal> _taxRates;

    public LocalTaxProvider()
        : this(new TaxTableOptions())
    {
    }

    public LocalTaxProvider(TaxTableOptions options)
    {
        // Rebuild with ordinal-ignore-case lookup: configuration binding replaces
        // the dictionaries and does not preserve the comparer.
        _taxRates = new Dictionary<string, decimal>(options.Rates, StringComparer.OrdinalIgnoreCase);
        _categoryAdjustments = new Dictionary<string, decimal>(options.CategoryAdjustments, StringComparer.OrdinalIgnoreCase);
    }

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
        if (!_taxRates.TryGetValue(countryCode.ToUpperInvariant(), out decimal baseRate))
            // Default to Georgia rate if country not found
            baseRate = _taxRates["GE"];

        // Apply category adjustment if applicable
        decimal adjustedRate = baseRate;
        if (!string.IsNullOrWhiteSpace(category) &&
            _categoryAdjustments.TryGetValue(category, out decimal adjustment))
            adjustedRate *= adjustment;

        decimal taxAmount = Math.Round(amount * adjustedRate, 2);

        string taxType = DetermineTaxType(countryCode);
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
