#region

using System.Globalization;

#endregion

namespace NetCommerce.SharedKernel.Domain;

/// <summary>
///     Immutable price breakdown capturing the "Triple-Pass Pricing Pattern" for audit compliance.
///     Ensures complete transparency on how a final price was calculated at a specific point in time.
///     2025 Elite Refinement: Stores LINE-ITEM TOTALS to avoid penny variance from division.
/// </summary>
public sealed class PriceBreakdown : ValueObject
{
    /// <summary>
    ///     Parameterless constructor for EF Core (uses reflection to set properties).
    /// </summary>
    private PriceBreakdown()
    {
        // EF Core will use reflection to set properties
        TaxType = string.Empty;
        Currency = string.Empty;
    }

    /// <summary>
    ///     JSON constructor for deserialization (e.g., saga state persistence).
    /// </summary>
    public PriceBreakdown(
        decimal basePrice,
        decimal discountAmount,
        decimal taxAmount,
        decimal taxRate,
        string taxType,
        string currency,
        int quantity = 1,
        decimal? lineDiscountTotal = null,
        decimal? lineTaxTotal = null)
    {
        if (basePrice < 0)
            throw new ArgumentException("Base price cannot be negative", nameof(basePrice));
        if (discountAmount < 0)
            throw new ArgumentException("Discount amount cannot be negative", nameof(discountAmount));
        if (taxAmount < 0)
            throw new ArgumentException("Tax amount cannot be negative", nameof(taxAmount));
        if (taxRate < 0 || taxRate > 1)
            throw new ArgumentException("Tax rate must be between 0 and 1", nameof(taxRate));
        if (string.IsNullOrWhiteSpace(taxType))
            throw new ArgumentException("Tax type is required", nameof(taxType));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        BasePrice = Math.Round(basePrice, 2);
        DiscountAmount = Math.Round(discountAmount, 2);
        TaxAmount = Math.Round(taxAmount, 2);
        TaxRate = Math.Round(taxRate, 4); // Keep precision for rates like 0.185
        TaxType = taxType.ToUpperInvariant();
        Currency = currency.ToUpperInvariant();
        Quantity = quantity;

        // 2025 Elite Refinement: Store line totals to avoid penny variance
        LineDiscountTotal = Math.Round(lineDiscountTotal ?? discountAmount * quantity, 2);
        LineTaxTotal = Math.Round(lineTaxTotal ?? taxAmount * quantity, 2);
    }

    /// <summary>
    ///     The original price from the Catalog (source of truth) - PER UNIT.
    /// </summary>
    public decimal BasePrice { get; }

    /// <summary>
    ///     Discount amount PER UNIT (for backward compatibility).
    ///     For accurate totals, use LineDiscountTotal instead.
    /// </summary>
    public decimal DiscountAmount { get; }

    /// <summary>
    ///     Tax amount PER UNIT (for backward compatibility).
    ///     For accurate totals, use LineTaxTotal instead.
    /// </summary>
    public decimal TaxAmount { get; }

    /// <summary>
    ///     The tax rate applied (e.g., 0.18 for 18% VAT). Stored for legal audit purposes.
    /// </summary>
    public decimal TaxRate { get; }

    /// <summary>
    ///     The type of tax applied (e.g., "VAT", "SALES_TAX", "GST").
    /// </summary>
    public string TaxType { get; }

    /// <summary>
    ///     The currency code (e.g., "GEL", "USD", "EUR").
    /// </summary>
    public string Currency { get; }

    /// <summary>
    ///     Quantity of items this price breakdown applies to.
    /// </summary>
    public int Quantity { get; }

    /// <summary>
    ///     2025 Elite Refinement: LINE-ITEM TOTAL discount to avoid penny variance.
    ///     This is the PRIMARY source of truth for discount totals.
    /// </summary>
    public decimal LineDiscountTotal { get; }

    /// <summary>
    ///     2025 Elite Refinement: LINE-ITEM TOTAL tax to avoid penny variance.
    ///     This is the PRIMARY source of truth for tax totals.
    /// </summary>
    public decimal LineTaxTotal { get; }

    /// <summary>
    ///     LINE-ITEM SUBTOTAL: (BasePrice * Quantity) - LineDiscountTotal
    /// </summary>
    public decimal LineSubTotal => Math.Round(BasePrice * Quantity - LineDiscountTotal, 2);

    /// <summary>
    ///     LINE-ITEM TOTAL: LineSubTotal + LineTaxTotal
    /// </summary>
    public decimal LineTotal => Math.Round(LineSubTotal + LineTaxTotal, 2);

    /// <summary>
    ///     The final price PER UNIT after applying discounts and adding taxes (for backward compatibility).
    ///     Formula: (BasePrice - DiscountAmount) + TaxAmount
    /// </summary>
    public decimal FinalPrice => Math.Round(BasePrice - DiscountAmount + TaxAmount, 2);

    /// <summary>
    ///     The subtotal PER UNIT before tax is applied (after discounts, for backward compatibility).
    ///     Formula: BasePrice - DiscountAmount
    /// </summary>
    public decimal SubTotal => Math.Round(BasePrice - DiscountAmount, 2);

    /// <summary>
    ///     Factory method to create a breakdown with explicit per-unit values (legacy pattern).
    ///     For 2025 best practices, use CreateFromLineTotals instead.
    /// </summary>
    public static PriceBreakdown Create(
        decimal basePrice,
        decimal discountAmount,
        decimal taxAmount,
        decimal taxRate,
        string taxType,
        string currency = "GEL")
    {
        return new PriceBreakdown(basePrice, discountAmount, taxAmount, taxRate, taxType, currency, 1);
    }

    /// <summary>
    ///     2025 Elite Factory Method: Creates a breakdown from LINE-ITEM TOTALS to avoid penny variance.
    ///     This is the recommended approach for financial systems.
    /// </summary>
    /// <param name="basePrice">Price per unit from catalog</param>
    /// <param name="quantity">Number of items</param>
    /// <param name="lineDiscountTotal">Total discount for all items (NOT divided)</param>
    /// <param name="lineTaxTotal">Total tax for all items (NOT divided)</param>
    /// <param name="taxRate">Tax rate for audit trail</param>
    /// <param name="taxType">Tax type (VAT, SALES_TAX, etc.)</param>
    /// <param name="currency">Currency code</param>
    public static PriceBreakdown CreateFromLineTotals(
        decimal basePrice,
        int quantity,
        decimal lineDiscountTotal,
        decimal lineTaxTotal,
        decimal taxRate,
        string taxType,
        string currency = "GEL")
    {
        // Calculate per-unit values for backward compatibility (but they're not the source of truth)
        decimal unitDiscount = quantity > 0 ? lineDiscountTotal / quantity : 0;
        decimal unitTax = quantity > 0 ? lineTaxTotal / quantity : 0;

        return new PriceBreakdown(
            basePrice,
            unitDiscount,
            unitTax,
            taxRate,
            taxType,
            currency,
            quantity,
            lineDiscountTotal,
            lineTaxTotal);
    }

    /// <summary>
    ///     Factory method for simple pricing with no discount or tax.
    /// </summary>
    public static PriceBreakdown CreateSimple(decimal basePrice, string currency = "GEL")
    {
        return new PriceBreakdown(basePrice, 0, 0, 0, "NONE", currency);
    }

    /// <summary>
    ///     Converts to Money value object (for backward compatibility).
    /// </summary>
    public Money ToMoney()
    {
        return Money.Create(FinalPrice, Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BasePrice;
        yield return DiscountAmount;
        yield return TaxAmount;
        yield return TaxRate;
        yield return TaxType;
        yield return Currency;
        yield return Quantity;
        yield return LineDiscountTotal;
        yield return LineTaxTotal;
    }

    public override string ToString()
    {
        if (DiscountAmount == 0 && TaxAmount == 0)
            return $"{FinalPrice.ToString("N2", CultureInfo.InvariantCulture)} {Currency}";

        return $"{FinalPrice.ToString("N2", CultureInfo.InvariantCulture)} {Currency} " +
               $"(Base: {BasePrice.ToString("N2", CultureInfo.InvariantCulture)}, " +
               $"Discount: -{DiscountAmount.ToString("N2", CultureInfo.InvariantCulture)}, " +
               $"Tax: +{TaxAmount.ToString("N2", CultureInfo.InvariantCulture)})";
    }
}
