using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Services;
using NetCommerce.SharedKernel.Domain;
using Shouldly;

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
        var basePrice = 100m;
        var quantity = 1;
        var customerId = Guid.NewGuid();
        var country = "GE";

        // Act - Pass 1: Base Price (from catalog)
        var catalogPrice = basePrice;

        // Pass 2: Apply Promotions
        var promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            null); // No coupon

        var subTotal = (catalogPrice * quantity) - promotionResult.DiscountAmount;

        // Pass 3: Calculate Taxes
        var taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        var unitDiscount = promotionResult.DiscountAmount / quantity;
        var unitTax = taxResult.Amount / quantity;

        var priceBreakdown = PriceBreakdown.Create(
            catalogPrice,
            unitDiscount,
            unitTax,
            taxResult.Rate,
            taxResult.Type,
            "GEL");

        // Assert
        priceBreakdown.BasePrice.ShouldBe(100m);
        priceBreakdown.DiscountAmount.ShouldBe(0m);
        priceBreakdown.TaxAmount.ShouldBe(18m);    // 18% VAT in Georgia
        priceBreakdown.TaxRate.ShouldBe(0.18m);
        priceBreakdown.FinalPrice.ShouldBe(118m);  // 100 + 18
    }

    [Fact]
    public async Task CompleteTriplePassPricing_WithDiscountAndTax_ShouldCalculateCorrectly()
    {
        // Arrange
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        var basePrice = 100m;
        var quantity = 1;
        var customerId = Guid.NewGuid();
        var country = "GE";
        var couponCode = "SAVE20"; // 20% discount

        // Act - Triple-Pass Pricing
        // Pass 1: Base Price
        var catalogPrice = basePrice;

        // Pass 2: Apply Promotions
        var promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            couponCode);

        var subTotal = (catalogPrice * quantity) - promotionResult.DiscountAmount;

        // Pass 3: Calculate Taxes
        var taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        var unitDiscount = promotionResult.DiscountAmount / quantity;
        var unitTax = taxResult.Amount / quantity;

        var priceBreakdown = PriceBreakdown.Create(
            catalogPrice,
            unitDiscount,
            unitTax,
            taxResult.Rate,
            taxResult.Type,
            "GEL");

        // Assert
        priceBreakdown.BasePrice.ShouldBe(100m);
        priceBreakdown.DiscountAmount.ShouldBe(20m);    // 20% of 100
        priceBreakdown.SubTotal.ShouldBe(80m);          // 100 - 20
        priceBreakdown.TaxAmount.ShouldBe(14.4m);       // 18% of 80
        priceBreakdown.FinalPrice.ShouldBe(94.4m);      // 80 + 14.4
    }

    [Fact]
    public async Task CompleteTriplePassPricing_WithReducedTaxCategory_ShouldCalculateCorrectly()
    {
        // Arrange
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        var basePrice = 10m;
        var quantity = 1;
        var customerId = Guid.NewGuid();
        var country = "GE";
        var category = "FOOD"; // Food has reduced tax rate (50% reduction)

        // Act - Triple-Pass Pricing
        var catalogPrice = basePrice;

        var promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            null);

        var subTotal = (catalogPrice * quantity) - promotionResult.DiscountAmount;

        var taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            category);

        var unitDiscount = promotionResult.DiscountAmount / quantity;
        var unitTax = taxResult.Amount / quantity;

        var priceBreakdown = PriceBreakdown.Create(
            catalogPrice,
            unitDiscount,
            unitTax,
            taxResult.Rate,
            taxResult.Type,
            "GEL");

        // Assert
        priceBreakdown.BasePrice.ShouldBe(10m);
        priceBreakdown.TaxAmount.ShouldBe(0.9m);     // 9% (18% * 50%)
        priceBreakdown.TaxRate.ShouldBe(0.09m);
        priceBreakdown.FinalPrice.ShouldBe(10.9m);   // 10 + 0.9
    }

    [Fact]
    public async Task CompleteTriplePassPricing_RealWorldScenario_ShouldCalculateCorrectly()
    {
        // Arrange - Real Georgian e-commerce scenario
        var taxProvider = new LocalTaxProvider();
        var promotionEngine = new SimplePromotionEngine();

        var productId = Guid.NewGuid();
        var basePrice = 2500m; // Laptop
        var quantity = 1;
        var customerId = Guid.NewGuid();
        var country = "GE";
        var couponCode = "FIRSTORDER"; // 25% discount

        // Act - Complete Triple-Pass Pricing
        var catalogPrice = basePrice;

        var promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            couponCode);

        var subTotal = (catalogPrice * quantity) - promotionResult.DiscountAmount;

        var taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        var unitDiscount = promotionResult.DiscountAmount / quantity;
        var unitTax = taxResult.Amount / quantity;

        var priceBreakdown = PriceBreakdown.Create(
            catalogPrice,
            unitDiscount,
            unitTax,
            taxResult.Rate,
            taxResult.Type,
            "GEL");

        // Assert - Verify complete pricing breakdown
        priceBreakdown.BasePrice.ShouldBe(2500m);
        priceBreakdown.DiscountAmount.ShouldBe(625m);      // 25% of 2500
        priceBreakdown.SubTotal.ShouldBe(1875m);           // 2500 - 625
        priceBreakdown.TaxAmount.ShouldBe(337.5m);         // 18% of 1875
        priceBreakdown.FinalPrice.ShouldBe(2212.5m);       // 1875 + 337.5
        
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
        var basePrice = 50m;
        var quantity = 3;
        var customerId = Guid.NewGuid();
        var country = "GE";
        var couponCode = "WELCOME10"; // 10% discount

        // Act - Triple-Pass Pricing
        var catalogPrice = basePrice;

        // Pass 2: Apply Promotions (on total)
        var promotionResult = await promotionEngine.CalculateDiscountAsync(
            productId,
            catalogPrice,
            quantity,
            customerId,
            couponCode);

        var subTotal = (catalogPrice * quantity) - promotionResult.DiscountAmount;

        // Pass 3: Calculate Taxes (on discounted total)
        var taxResult = await taxProvider.GetTaxAsync(
            subTotal,
            country,
            "ELECTRONICS");

        // Per-unit calculations
        var unitDiscount = promotionResult.DiscountAmount / quantity;
        var unitTax = taxResult.Amount / quantity;

        var priceBreakdown = PriceBreakdown.Create(
            catalogPrice,
            unitDiscount,
            unitTax,
            taxResult.Rate,
            taxResult.Type,
            "GEL");

        // Assert
        // Total before discount: 50 * 3 = 150
        // Discount: 150 * 0.10 = 15
        // Subtotal: 150 - 15 = 135
        // Tax: 135 * 0.18 = 24.3
        // Final: 135 + 24.3 = 159.3

        priceBreakdown.BasePrice.ShouldBe(50m);
        priceBreakdown.DiscountAmount.ShouldBe(5m);     // 15 / 3
        priceBreakdown.SubTotal.ShouldBe(45m);          // 50 - 5
        priceBreakdown.TaxAmount.ShouldBe(8.1m);        // 24.3 / 3
        priceBreakdown.FinalPrice.ShouldBe(53.1m);      // 45 + 8.1
        
        // Verify total for 3 units
        (priceBreakdown.FinalPrice * quantity).ShouldBe(159.3m);
    }
}
