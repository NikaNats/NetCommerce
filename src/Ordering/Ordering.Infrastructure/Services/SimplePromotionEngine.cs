#region

using NetCommerce.Ordering.Application.Orders.Services;

#endregion

namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Simple promotion engine implementation with basic discount rules.
///     In production, this would integrate with a sophisticated rules engine or marketing platform.
/// </summary>
public sealed class SimplePromotionEngine : IPromotionEngine
{
    // Simple coupon code table - in production, this would be in a database
    private readonly Dictionary<string, CouponRule> _coupons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WELCOME10"] = new CouponRule(0.10m, "Welcome 10% Off"),
        ["SAVE20"] = new CouponRule(0.20m, "Save 20%"),
        ["SUMMER15"] = new CouponRule(0.15m, "Summer Sale 15%"),
        ["FIRSTORDER"] = new CouponRule(0.25m, "First Order 25% Off")
    };

    public Task<PromotionResult> CalculateDiscountAsync(
        Guid productId,
        decimal basePrice,
        int quantity,
        Guid customerId,
        string? couponCode = null,
        CancellationToken cancellationToken = default)
    {
        if (basePrice <= 0 || quantity <= 0)
            return Task.FromResult(PromotionResult.NoDiscount());

        decimal totalPrice = basePrice * quantity;

        // Check for coupon code discount
        if (!string.IsNullOrWhiteSpace(couponCode) &&
            _coupons.TryGetValue(couponCode, out CouponRule? coupon))
        {
            decimal discountAmount = Math.Round(totalPrice * coupon.DiscountPercentage, 2);
            return Task.FromResult(PromotionResult.WithDiscount(
                discountAmount,
                coupon.Name,
                couponCode));
        }

        // Future: Add automatic promotions based on:
        // - Customer loyalty tier
        // - Product category promotions
        // - Bulk purchase discounts
        // - Time-based flash sales

        return Task.FromResult(PromotionResult.NoDiscount());
    }

    private sealed record CouponRule(decimal DiscountPercentage, string Name);
}
