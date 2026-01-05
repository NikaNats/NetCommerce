#nullable enable

namespace NetCommerce.SharedKernel.Domain;

/// <summary>
///     Immutable price breakdown capturing the "Triple-Pass Pricing Pattern" for audit compliance.
///     Ensures complete transparency on how a final price was calculated at a specific point in time.
/// </summary>
public sealed class PriceBreakdown : ValueObject
{
    /// <summary>
    ///     JSON constructor for deserialization (e.g., saga state persistence).
    /// </summary>
    public PriceBreakdown(
        decimal basePrice,
        decimal discountAmount,
        decimal taxAmount,
        decimal taxRate,
        string taxType,
        string currency)
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

        BasePrice = Math.Round(basePrice, 2);
        DiscountAmount = Math.Round(discountAmount, 2);
        TaxAmount = Math.Round(taxAmount, 2);
        TaxRate = Math.Round(taxRate, 4); // Keep precision for rates like 0.185
        TaxType = taxType.ToUpperInvariant();
        Currency = currency.ToUpperInvariant();
    }

    /// <summary>
    ///     The original price from the Catalog (source of truth).
    /// </summary>
    public decimal BasePrice { get; }

    /// <summary>
    ///     Total deduction from promotions, discounts, and coupons.
    /// </summary>
    public decimal DiscountAmount { get; }

    /// <summary>
    ///     The calculated tax amount based on jurisdiction and product category.
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
    ///     The final price after applying discounts and adding taxes.
    ///     Formula: (BasePrice - DiscountAmount) + TaxAmount
    /// </summary>
    public decimal FinalPrice => Math.Round((BasePrice - DiscountAmount) + TaxAmount, 2);

    /// <summary>
    ///     The subtotal before tax is applied (after discounts).
    ///     Formula: BasePrice - DiscountAmount
    /// </summary>
    public decimal SubTotal => Math.Round(BasePrice - DiscountAmount, 2);

    /// <summary>
    ///     Factory method to create a breakdown with explicit values.
    /// </summary>
    public static PriceBreakdown Create(
        decimal basePrice,
        decimal discountAmount,
        decimal taxAmount,
        decimal taxRate,
        string taxType,
        string currency = "GEL")
    {
        return new PriceBreakdown(basePrice, discountAmount, taxAmount, taxRate, taxType, currency);
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
    }

    public override string ToString()
    {
        if (DiscountAmount == 0 && TaxAmount == 0)
            return $"{FinalPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

        return $"{FinalPrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)} {Currency} " +
               $"(Base: {BasePrice.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}, " +
               $"Discount: -{DiscountAmount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}, " +
               $"Tax: +{TaxAmount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)})";
    }
}
