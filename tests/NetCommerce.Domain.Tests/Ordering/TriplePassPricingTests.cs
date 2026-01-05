#region

using NetCommerce.Ordering.Application.Orders.Services;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Services;
using NetCommerce.SharedKernel.Domain;

#endregion

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Integration tests for the Triple-Pass Pricing Pattern.
///     Tests the complete pricing flow: Catalog Price → Promotions → Tax Calculation.
/// </summary>
public class TriplePassPricingTests
{
    [Fact]
    public async Task CompleteTriplePassPricing_WithoutDiscountOrTax_ShouldCalculateCorrectly()
    {
        // Arrange
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        decimal basePrice = 100m;
        int quantity = 1;
        var customerId = Guid.NewGuid();
        string country = "GE";

        // Act - Pass 1: Base Price (from catalog)
        decimal catalogPrice = basePrice;

        // Pass 2: Apply Promotions
        PromotionResult promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId); // No coupon

        decimal subTotal = catalogPrice * quantity - promotionResult.DiscountAmount;

        // Pass 3: Calculate Taxes
        TaxCalculationResult taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        // 2025 Elite Refinement: Use line totals to avoid penny variance
        var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
            catalogPrice,
            quantity,
            promotionResult.DiscountAmount,
            taxResult.Amount,
            taxResult.Rate,
            taxResult.Type);

        // Assert
        priceBreakdown.BasePrice.ShouldBe(100m);
        priceBreakdown.DiscountAmount.ShouldBe(0m);
        priceBreakdown.TaxAmount.ShouldBe(18m); // 18% VAT in Georgia
        priceBreakdown.TaxRate.ShouldBe(0.18m);
        priceBreakdown.FinalPrice.ShouldBe(118m); // 100 + 18
    }

    [Fact]
    public async Task CompleteTriplePassPricing_WithDiscountAndTax_ShouldCalculateCorrectly()
    {
        // Arrange
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        decimal basePrice = 100m;
        int quantity = 1;
        var customerId = Guid.NewGuid();
        string country = "GE";
        string couponCode = "SAVE20"; // 20% discount

        // Act - Triple-Pass Pricing
        // Pass 1: Base Price
        decimal catalogPrice = basePrice;

        // Pass 2: Apply Promotions
        PromotionResult promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            couponCode);

        decimal subTotal = catalogPrice * quantity - promotionResult.DiscountAmount;

        // Pass 3: Calculate Taxes
        TaxCalculationResult taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        // 2025 Elite Refinement: Use line totals to avoid penny variance
        var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
            catalogPrice,
            quantity,
            promotionResult.DiscountAmount,
            taxResult.Amount,
            taxResult.Rate,
            taxResult.Type);

        // Assert
        priceBreakdown.BasePrice.ShouldBe(100m);
        priceBreakdown.DiscountAmount.ShouldBe(20m); // 20% of 100
        priceBreakdown.SubTotal.ShouldBe(80m); // 100 - 20
        priceBreakdown.TaxAmount.ShouldBe(14.4m); // 18% of 80
        priceBreakdown.FinalPrice.ShouldBe(94.4m); // 80 + 14.4
        priceBreakdown.LineDiscountTotal.ShouldBe(20m); // Line total
        priceBreakdown.LineTaxTotal.ShouldBe(14.4m); // Line total
    }

    [Fact]
    public async Task CompleteTriplePassPricing_WithReducedTaxCategory_ShouldCalculateCorrectly()
    {
        // Arrange
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        decimal basePrice = 10m;
        int quantity = 1;
        var customerId = Guid.NewGuid();
        string country = "GE";
        string category = "FOOD"; // Food has reduced tax rate (50% reduction)

        // Act - Triple-Pass Pricing
        decimal catalogPrice = basePrice;

        PromotionResult promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId);

        decimal subTotal = catalogPrice * quantity - promotionResult.DiscountAmount;

        TaxCalculationResult taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            category);

        // 2025 Elite Refinement: Use line totals to avoid penny variance
        var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
            catalogPrice,
            quantity,
            promotionResult.DiscountAmount,
            taxResult.Amount,
            taxResult.Rate,
            taxResult.Type);

        // Assert
        priceBreakdown.BasePrice.ShouldBe(10m);
        priceBreakdown.TaxAmount.ShouldBe(0.9m); // 9% (18% * 50%)
        priceBreakdown.TaxRate.ShouldBe(0.09m);
        priceBreakdown.FinalPrice.ShouldBe(10.9m); // 10 + 0.9
    }

    [Fact]
    public async Task CompleteTriplePassPricing_RealWorldScenario_ShouldCalculateCorrectly()
    {
        // Arrange - Real Georgian e-commerce scenario
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        decimal basePrice = 2500m; // Laptop
        int quantity = 1;
        var customerId = Guid.NewGuid();
        string country = "GE";
        string couponCode = "FIRSTORDER"; // 25% discount

        // Act - Complete Triple-Pass Pricing
        decimal catalogPrice = basePrice;

        PromotionResult promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            couponCode);

        decimal subTotal = catalogPrice * quantity - promotionResult.DiscountAmount;

        TaxCalculationResult taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        // 2025 Elite Refinement: Use line totals to avoid penny variance
        var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
            catalogPrice,
            quantity,
            promotionResult.DiscountAmount,
            taxResult.Amount,
            taxResult.Rate,
            taxResult.Type);

        // Assert - Verify complete pricing breakdown
        priceBreakdown.BasePrice.ShouldBe(2500m);
        priceBreakdown.DiscountAmount.ShouldBe(625m); // 25% of 2500
        priceBreakdown.SubTotal.ShouldBe(1875m); // 2500 - 625
        priceBreakdown.TaxAmount.ShouldBe(337.5m); // 18% of 1875
        priceBreakdown.FinalPrice.ShouldBe(2212.5m); // 1875 + 337.5

        // Verify audit trail
        promotionResult.AppliedPromotionName.ShouldBe("First Order 25% Off");
        taxResult.Type.ShouldBe("VAT");
        taxResult.Rate.ShouldBe(0.18m);
        taxResult.ProviderName.ShouldBe("LocalTaxProvider");
    }

    [Fact]
    public async Task CompleteTriplePassPricing_WithMultipleQuantity_ShouldCalculateEachUnitCorrectly()
    {
        // Arrange
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        decimal basePrice = 50m;
        int quantity = 3;
        var customerId = Guid.NewGuid();
        string country = "GE";
        string couponCode = "WELCOME10"; // 10% discount

        // Act - Triple-Pass Pricing
        decimal catalogPrice = basePrice;

        // Pass 2: Apply Promotions (on total)
        PromotionResult promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            couponCode);

        decimal subTotal = catalogPrice * quantity - promotionResult.DiscountAmount;

        // Pass 3: Calculate Taxes (on discounted total)
        TaxCalculationResult taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        // 2025 Elite Refinement: Store line totals directly to avoid penny variance
        var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
            catalogPrice,
            quantity,
            promotionResult.DiscountAmount,
            taxResult.Amount,
            taxResult.Rate,
            taxResult.Type);

        // Assert
        // Total before discount: 50 * 3 = 150
        // Discount: 150 * 0.10 = 15
        // Subtotal: 150 - 15 = 135
        // Tax: 135 * 0.18 = 24.3
        // Final: 135 + 24.3 = 159.3

        priceBreakdown.BasePrice.ShouldBe(50m);
        priceBreakdown.DiscountAmount.ShouldBe(5m); // 15 / 3 (per unit, backward compat)
        priceBreakdown.SubTotal.ShouldBe(45m); // 50 - 5
        priceBreakdown.TaxAmount.ShouldBe(8.1m); // 24.3 / 3 (per unit, backward compat)
        priceBreakdown.FinalPrice.ShouldBe(53.1m); // 45 + 8.1

        // 2025 Elite: Verify line totals are stored EXACTLY without division
        priceBreakdown.LineDiscountTotal.ShouldBe(15m); // No penny variance!
        priceBreakdown.LineTaxTotal.ShouldBe(24.3m); // No penny variance!
        priceBreakdown.LineSubTotal.ShouldBe(135m); // 150 - 15
        priceBreakdown.LineTotal.ShouldBe(159.3m); // 135 + 24.3
    }

    [Fact]
    public async Task PennyVariance_WithDivisionByThree_ShouldBeAvoided()
    {
        // Arrange - Classic penny variance scenario: $10.00 / 3 items
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        decimal basePrice = 10m;
        int quantity = 3; // Division by 3 creates rounding issues
        var customerId = Guid.NewGuid();
        string country = "GE";

        // Act - Triple-Pass Pricing
        decimal catalogPrice = basePrice;

        PromotionResult promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId);

        decimal subTotal = catalogPrice * quantity - promotionResult.DiscountAmount;

        TaxCalculationResult taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        // 2025 Elite: Store line totals to avoid penny variance
        var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
            catalogPrice,
            quantity,
            promotionResult.DiscountAmount,
            taxResult.Amount,
            taxResult.Rate,
            taxResult.Type);

        // Assert - Verify NO penny variance
        // Old approach: $10.00 discount / 3 = $3.3333... → $3.33 per unit → $9.99 total (WRONG!)
        // New approach: Store $10.00 directly as line total (CORRECT!)

        priceBreakdown.LineDiscountTotal.ShouldBe(0m); // No discount in this test
        priceBreakdown.LineTaxTotal.ShouldBe(5.4m); // 30 * 0.18 = 5.4
        priceBreakdown.LineSubTotal.ShouldBe(30m); // 3 * 10
        priceBreakdown.LineTotal.ShouldBe(35.4m); // 30 + 5.4

        // Verify per-unit calculations are backward compatible
        priceBreakdown.BasePrice.ShouldBe(10m);
        priceBreakdown.TaxAmount.ShouldBe(1.8m); // 5.4 / 3
    }

    [Fact]
    public async Task PennyVariance_WithSevenItems_ShouldBeAvoided()
    {
        // Arrange - Another problematic quantity: 7 items with discount
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        decimal basePrice = 15m;
        int quantity = 7;
        var customerId = Guid.NewGuid();
        string country = "GE";
        string couponCode = "SAVE20";

        // Act
        decimal catalogPrice = basePrice;

        PromotionResult promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            couponCode);

        decimal subTotal = catalogPrice * quantity - promotionResult.DiscountAmount;

        TaxCalculationResult taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        var priceBreakdown = PriceBreakdown.CreateFromLineTotals(
            catalogPrice,
            quantity,
            promotionResult.DiscountAmount,
            taxResult.Amount,
            taxResult.Rate,
            taxResult.Type);

        // Assert
        // Base: 7 * 15 = 105
        // Discount: 105 * 0.20 = 21
        // Subtotal: 105 - 21 = 84
        // Tax: 84 * 0.18 = 15.12
        // Final: 84 + 15.12 = 99.12

        priceBreakdown.LineDiscountTotal.ShouldBe(21m); // Exact line total
        priceBreakdown.LineTaxTotal.ShouldBe(15.12m); // Exact line total
        priceBreakdown.LineSubTotal.ShouldBe(84m);
        priceBreakdown.LineTotal.ShouldBe(99.12m);

        // The key is: We store EXACT totals, not derived values
    }

    [Fact]
    public void Calculate_WithDiscountAndTax_ShouldMatchLedger()
    {
        // Arrange
        decimal basePrice = 100m;
        int quantity = 2;
        decimal discount = 20m; // Total discount
        decimal taxRate = 0.18m; // 18%

        // Logic:
        // 1. Line Total: 200
        // 2. Discounted: 180
        // 3. Taxable: 180 * 0.18 = 32.4
        // 4. Final: 180 + 32.4 = 212.4

        // Act
        var breakdown = PriceBreakdown.CreateFromLineTotals(
            basePrice,
            quantity,
            discount,
            32.4m,
            taxRate,
            "VAT",
            "USD");

        // Assert
        breakdown.LineSubTotal.ShouldBe(180m);
        breakdown.LineTotal.ShouldBe(212.4m);
        breakdown.LineTaxTotal.ShouldBe(32.4m);
    }

    [Fact]
    public void PennyVariance_DivisionByThree_ShouldNotLoseMoney()
    {
        // Arrange: $10.00 split 3 ways. Old systems lose a penny.
        decimal basePrice = 10m;
        int quantity = 3;

        // Act
        var breakdown = PriceBreakdown.CreateFromLineTotals(
            basePrice,
            quantity,
            0,
            5.4m, // 30 * 0.18
            0.18m, "VAT", "USD");

        // Assert
        breakdown.LineTotal.ShouldBe(35.4m); // Exact 35.40, not 35.39999
    }
}
