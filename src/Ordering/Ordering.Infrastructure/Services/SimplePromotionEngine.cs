#region

using NetCommerce.Ordering.Application.Orders.Services;

#endregion

namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Simple promotion engine implementation with basic discount rules.
///     Coupon rules come from <see cref="PromotionOptions"/> (config-overridable);
///     Production-like environments must acknowledge them (see validator).
/// </summary>
public sealed class SimplePromotionEngine : IPromotionEngine
{
    private readonly Dictionary<string, CouponRule> _coupons;

    public SimplePromotionEngine()
        : this(new PromotionOptions())
    {
    }

    public SimplePromotionEngine(PromotionOptions options)
    {
        _coupons = new Dictionary<string, CouponRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, fraction) in options.Coupons)
        {
            options.CouponNames.TryGetValue(code, out var name);
            _coupons[code] = new CouponRule(fraction, name ?? code);
        }
    }

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
