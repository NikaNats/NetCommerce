#region

using NetCommerce.Domain.Shared;

#endregion

namespace NetCommerce.Domain.Tests.SharedKernel;

/// <summary>
///     Tests for PriceBreakdown value object ensuring Triple-Pass Pricing Pattern correctness.
/// </summary>
public class PriceBreakdownTests
{
    [Fact]
    public void Create_WithValidValues_ShouldCreatePriceBreakdown()
    {
        // Arrange
        decimal basePrice = 100m;
        decimal discount = 10m;
        decimal taxAmount = 16.2m; // 18% of (100-10) = 18% of 90
        decimal taxRate = 0.18m;
        string taxType = "VAT";
        string currency = "GEL";

        // Act
        var breakdown = PriceBreakdown.Create(basePrice, discount, taxAmount, taxRate, taxType, currency);

        // Assert
        breakdown.ShouldNotBeNull();
        breakdown.BasePrice.ShouldBe(100m);
        breakdown.DiscountAmount.ShouldBe(10m);
        breakdown.TaxAmount.ShouldBe(16.2m);
        breakdown.TaxRate.ShouldBe(0.18m);
        breakdown.TaxType.ShouldBe("VAT");
        breakdown.Currency.ShouldBe("GEL");
        breakdown.SubTotal.ShouldBe(90m); // 100 - 10
        breakdown.FinalPrice.ShouldBe(106.2m); // 90 + 16.2
    }

    [Fact]
    public void Create_WithNegativeBasePrice_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
                PriceBreakdown.Create(-100m, 0, 0, 0, "NONE"))
            .Message.ShouldContain("Base price cannot be negative");
    }

    [Fact]
    public void Create_WithNegativeDiscount_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
                PriceBreakdown.Create(100m, -10m, 0, 0, "NONE"))
            .Message.ShouldContain("Discount amount cannot be negative");
    }

    [Fact]
    public void Create_WithNegativeTax_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
                PriceBreakdown.Create(100m, 0, -10m, 0, "NONE"))
            .Message.ShouldContain("Tax amount cannot be negative");
    }

    [Fact]
    public void Create_WithInvalidTaxRate_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
                PriceBreakdown.Create(100m, 0, 0, -0.1m, "NONE"))
            .Message.ShouldContain("Tax rate must be between 0 and 1");

        Should.Throw<ArgumentException>(() =>
                PriceBreakdown.Create(100m, 0, 0, 1.5m, "NONE"))
            .Message.ShouldContain("Tax rate must be between 0 and 1");
    }

    [Fact]
    public void Create_WithEmptyTaxType_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
                PriceBreakdown.Create(100m, 0, 0, 0, ""))
            .Message.ShouldContain("Tax type is required");
    }

    [Fact]
    public void Create_WithEmptyCurrency_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
                PriceBreakdown.Create(100m, 0, 0, 0, "NONE", ""))
            .Message.ShouldContain("Currency is required");
    }

    [Fact]
    public void CreateSimple_ShouldCreateBreakdownWithoutDiscountOrTax()
    {
        // Act
        var breakdown = PriceBreakdown.CreateSimple(100m, "USD");

        // Assert
        breakdown.BasePrice.ShouldBe(100m);
        breakdown.DiscountAmount.ShouldBe(0m);
        breakdown.TaxAmount.ShouldBe(0m);
        breakdown.TaxRate.ShouldBe(0m);
        breakdown.TaxType.ShouldBe("NONE");
        breakdown.Currency.ShouldBe("USD");
        breakdown.FinalPrice.ShouldBe(100m);
        breakdown.SubTotal.ShouldBe(100m);
    }

    [Fact]
    public void FinalPrice_WithDiscountAndTax_ShouldCalculateCorrectly()
    {
        // Arrange - Real-world scenario
        var breakdown = PriceBreakdown.Create(
            120m,
            20m, // 20 GEL discount
            18m, // 18% VAT on (120-20)
            0.18m,
            "VAT");

        // Assert
        breakdown.SubTotal.ShouldBe(100m); // 120 - 20
        breakdown.FinalPrice.ShouldBe(118m); // 100 + 18
    }

    [Fact]
    public void ToMoney_ShouldConvertToMoneyValueObject()
    {
        // Arrange
        var breakdown = PriceBreakdown.Create(100m, 10m, 16.2m, 0.18m, "VAT");

        // Act
        var money = breakdown.ToMoney();

        // Assert
        money.ShouldNotBeNull();
        money.Amount.ShouldBe(106.2m);
        money.Currency.ShouldBe("GEL");
    }

    [Theory]
    [InlineData(100, 0, 0, 0, "NONE", "GEL", "100.00 GEL")]
    [InlineData(100, 10, 16.2, 0.18, "VAT", "GEL", "106.20 GEL (Base: 100.00, Discount: -10.00, Tax: +16.20)")]
    public void ToString_ShouldFormatCorrectly(
        decimal basePrice,
        decimal discount,
        decimal tax,
        decimal taxRate,
        string taxType,
        string currency,
        string expected)
    {
        // Arrange
        var breakdown = PriceBreakdown.Create(basePrice, discount, tax, taxRate, taxType, currency);

        // Act
        string result = breakdown.ToString();

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void EqualityComparison_WithSameValues_ShouldBeEqual()
    {
        // Arrange
        var breakdown1 = PriceBreakdown.Create(100m, 10m, 16.2m, 0.18m, "VAT");
        var breakdown2 = PriceBreakdown.Create(100m, 10m, 16.2m, 0.18m, "VAT");

        // Assert
        breakdown1.ShouldBe(breakdown2);
        (breakdown1 == breakdown2).ShouldBeTrue();
    }

    [Fact]
    public void EqualityComparison_WithDifferentValues_ShouldNotBeEqual()
    {
        // Arrange
        var breakdown1 = PriceBreakdown.Create(100m, 10m, 16.2m, 0.18m, "VAT");
        var breakdown2 = PriceBreakdown.Create(100m, 20m, 14.4m, 0.18m, "VAT");

        // Assert
        breakdown1.ShouldNotBe(breakdown2);
        (breakdown1 != breakdown2).ShouldBeTrue();
    }

    [Fact]
    public void Rounding_ShouldWorkCorrectly()
    {
        // Arrange - Values that might cause rounding issues
        var breakdown = PriceBreakdown.Create(
            99.999m,
            9.999m,
            16.199m,
            0.1799m,
            "VAT");

        // Assert - Should round to 2 decimal places for prices, 4 for rates
        breakdown.BasePrice.ShouldBe(100.00m);
        breakdown.DiscountAmount.ShouldBe(10.00m);
        breakdown.TaxAmount.ShouldBe(16.20m);
        breakdown.TaxRate.ShouldBe(0.1799m); // Rate keeps 4 decimal places
        breakdown.SubTotal.ShouldBe(90.00m);
        breakdown.FinalPrice.ShouldBe(106.20m);
    }
}
