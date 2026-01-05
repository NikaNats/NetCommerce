using NetCommerce.Ordering.Infrastructure.Services;
using Shouldly;

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Tests for SimplePromotionEngine ensuring discount calculations are correct.
/// </summary>
public class SimplePromotionEngineTests
{
    private readonly SimplePromotionEngine _engine;
    private readonly Guid _productId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    public SimplePromotionEngineTests()
    {
        _engine = new SimplePromotionEngine();
    }

    [Theory]
    [InlineData("WELCOME10", 100, 1, 10.00, "Welcome 10% Off")]
    [InlineData("SAVE20", 100, 1, 20.00, "Save 20%")]
    [InlineData("SUMMER15", 100, 1, 15.00, "Summer Sale 15%")]
    [InlineData("FIRSTORDER", 100, 1, 25.00, "First Order 25% Off")]
    public async Task CalculateDiscountAsync_WithValidCoupon_ShouldApplyDiscount(
        string couponCode,
        decimal basePrice,
        int quantity,
        decimal expectedDiscount,
        string expectedPromotionName)
    {
        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            basePrice,
            quantity,
            _customerId,
            couponCode);

        // Assert
        result.ShouldNotBeNull();
        result.DiscountAmount.ShouldBe(expectedDiscount);
        result.AppliedPromotionName.ShouldBe(expectedPromotionName);
        result.CouponCode.ShouldBe(couponCode);
    }

    [Fact]
    public async Task CalculateDiscountAsync_WithInvalidCoupon_ShouldReturnNoDiscount()
    {
        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            100m,
            1,
            _customerId,
            "INVALID_COUPON");

        // Assert
        result.DiscountAmount.ShouldBe(0m);
        result.AppliedPromotionName.ShouldBeNull();
        result.CouponCode.ShouldBeNull();
    }

    [Fact]
    public async Task CalculateDiscountAsync_WithNoCoupon_ShouldReturnNoDiscount()
    {
        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            100m,
            1,
            _customerId,
            null);

        // Assert
        result.DiscountAmount.ShouldBe(0m);
        result.AppliedPromotionName.ShouldBeNull();
        result.CouponCode.ShouldBeNull();
    }

    [Fact]
    public async Task CalculateDiscountAsync_WithMultipleQuantity_ShouldCalculateOnTotalPrice()
    {
        // Arrange
        var basePrice = 50m;
        var quantity = 3;
        var couponCode = "SAVE20"; // 20% off

        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            basePrice,
            quantity,
            _customerId,
            couponCode);

        // Assert
        // Total: 50 * 3 = 150
        // Discount: 150 * 0.20 = 30
        result.DiscountAmount.ShouldBe(30.00m);
        result.AppliedPromotionName.ShouldBe("Save 20%");
    }

    [Fact]
    public async Task CalculateDiscountAsync_WithZeroPrice_ShouldReturnNoDiscount()
    {
        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            0m,
            1,
            _customerId,
            "SAVE20");

        // Assert
        result.DiscountAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task CalculateDiscountAsync_WithNegativePrice_ShouldReturnNoDiscount()
    {
        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            -100m,
            1,
            _customerId,
            "SAVE20");

        // Assert
        result.DiscountAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task CalculateDiscountAsync_WithZeroQuantity_ShouldReturnNoDiscount()
    {
        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            100m,
            0,
            _customerId,
            "SAVE20");

        // Assert
        result.DiscountAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task CalculateDiscountAsync_CaseInsensitiveCoupon_ShouldWork()
    {
        // Act
        var result1 = await _engine.CalculateDiscountAsync(_productId, 100m, 1, _customerId, "save20");
        var result2 = await _engine.CalculateDiscountAsync(_productId, 100m, 1, _customerId, "SAVE20");
        var result3 = await _engine.CalculateDiscountAsync(_productId, 100m, 1, _customerId, "Save20");

        // Assert
        result1.DiscountAmount.ShouldBe(result2.DiscountAmount);
        result2.DiscountAmount.ShouldBe(result3.DiscountAmount);
        result1.DiscountAmount.ShouldBe(20.00m);
    }

    [Fact]
    public async Task CalculateDiscountAsync_WithRealWorldScenario_ShouldCalculateCorrectly()
    {
        // Arrange - Customer buying 2 items at 85.50 each with WELCOME10 coupon
        var basePrice = 85.50m;
        var quantity = 2;
        var couponCode = "WELCOME10";

        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            basePrice,
            quantity,
            _customerId,
            couponCode);

        // Assert
        // Total: 85.50 * 2 = 171.00
        // Discount: 171.00 * 0.10 = 17.10
        result.DiscountAmount.ShouldBe(17.10m);
        result.AppliedPromotionName.ShouldBe("Welcome 10% Off");
        result.CouponCode.ShouldBe(couponCode);
    }

    [Theory]
    [InlineData(99.99, "SAVE20", 19.998)]   // Should round to 20.00
    [InlineData(33.33, "WELCOME10", 3.333)] // Should round to 3.33
    public async Task CalculateDiscountAsync_WithRoundingScenarios_ShouldRoundCorrectly(
        decimal basePrice,
        string couponCode,
        decimal expectedBeforeRound)
    {
        // Act
        var result = await _engine.CalculateDiscountAsync(
            _productId,
            basePrice,
            1,
            _customerId,
            couponCode);

        // Assert
        result.DiscountAmount.ShouldBe(Math.Round(expectedBeforeRound, 2));
    }
}
