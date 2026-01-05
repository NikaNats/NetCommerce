namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Tax calculation strategy with circuit breaker support.
///     In production, this might call Avalara, TaxJar, or a similar external service.
///     A local fallback provider ensures checkout remains operational during API outages.
/// </summary>
public interface ITaxProvider
{
    /// <summary>
    ///     Calculates tax for a given amount based on jurisdiction and product category.
    /// </summary>
    /// <param name="amount">The taxable amount (after discounts).</param>
    /// <param name="countryCode">ISO country code (e.g., "GE" for Georgia, "US" for United States).</param>
    /// <param name="category">Product category for category-specific tax rules (e.g., "ELECTRONICS", "FOOD").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tax calculation result with amount, rate, type, and provider name.</returns>
    Task<TaxCalculationResult> GetTaxAsync(
        decimal amount,
        string countryCode,
        string? category,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Result of a tax calculation including audit metadata.
/// </summary>
public sealed record TaxCalculationResult(
    decimal Amount,
    decimal Rate,
    string Type,
    string ProviderName)
{
    /// <summary>
    ///     Creates a no-tax result for tax-exempt scenarios.
    /// </summary>
    public static TaxCalculationResult NoTax(string providerName = "NoTaxProvider")
    {
        return new TaxCalculationResult(0, 0, "NONE", providerName);
    }

    /// <summary>
    ///     Creates a VAT tax result.
    /// </summary>
    public static TaxCalculationResult Vat(decimal amount, decimal rate, string providerName)
    {
        return new TaxCalculationResult(amount, rate, "VAT", providerName);
    }

    /// <summary>
    ///     Creates a sales tax result.
    /// </summary>
    public static TaxCalculationResult SalesTax(decimal amount, decimal rate, string providerName)
    {
        return new TaxCalculationResult(amount, rate, "SALES_TAX", providerName);
    }
}
