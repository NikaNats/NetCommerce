#nullable enable
using System.Globalization;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Domain.Shared;

/// <summary>
///     Immutable price breakdown capturing the "Triple-Pass Pricing Pattern" for audit compliance.
///     NetCommerce-specific: Uses GEL as default currency and Georgian VAT structure.
/// </summary>
public sealed class PriceBreakdown : ValueObject
{
    private PriceBreakdown()
    {
        TaxType = string.Empty;
        Currency = string.Empty;
    }

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
        TaxRate = Math.Round(taxRate, 4);
        TaxType = taxType.ToUpperInvariant();
        Currency = currency.ToUpperInvariant();
        Quantity = quantity;

        LineDiscountTotal = Math.Round(lineDiscountTotal ?? discountAmount * quantity, 2);
        LineTaxTotal = Math.Round(lineTaxTotal ?? taxAmount * quantity, 2);
    }

    public decimal BasePrice { get; }
    public decimal DiscountAmount { get; }
    public decimal TaxAmount { get; }
    public decimal TaxRate { get; }
    public string TaxType { get; }
    public string Currency { get; }
    public int Quantity { get; }
    public decimal LineDiscountTotal { get; }
    public decimal LineTaxTotal { get; }

    public decimal LineSubTotal => Math.Round(BasePrice * Quantity - LineDiscountTotal, 2);
    public decimal LineTotal => Math.Round(LineSubTotal + LineTaxTotal, 2);
    public decimal FinalPrice => Math.Round(BasePrice - DiscountAmount + TaxAmount, 2);
    public decimal SubTotal => Math.Round(BasePrice - DiscountAmount, 2);

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

    public static PriceBreakdown CreateFromLineTotals(
        decimal basePrice,
        int quantity,
        decimal lineDiscountTotal,
        decimal lineTaxTotal,
        decimal taxRate,
        string taxType,
        string currency = "GEL")
    {
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

    public static PriceBreakdown CreateSimple(decimal basePrice, string currency = "GEL")
    {
        return new PriceBreakdown(basePrice, 0, 0, 0, "NONE", currency);
    }

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
