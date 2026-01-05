#nullable enable

namespace NetCommerce.Ordering.Application.Orders.Services;

/// <summary>
///     Promotion engine that evaluates active business rules and calculates discounts.
///     This service is architected to eventually move to a dedicated Pricing module.
/// </summary>
public interface IPromotionEngine
{
    /// <summary>
    ///     Calculates the discount amount for a product based on active promotions.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="basePrice">The base price before discount.</param>
    /// <param name="quantity">The quantity being purchased.</param>
    /// <param name="customerId">The customer identifier for personalized promotions.</param>
    /// <param name="couponCode">Optional coupon code for additional discounts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total discount amount to be applied.</returns>
    Task<PromotionResult> CalculateDiscountAsync(
        Guid productId,
        decimal basePrice,
        int quantity,
        Guid customerId,
        string? couponCode = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Result of a promotion calculation including audit information.
/// </summary>
public sealed record PromotionResult(
    decimal DiscountAmount,
    string? AppliedPromotionName,
    string? CouponCode)
{
    /// <summary>
    ///     Creates a result with no discount.
    /// </summary>
    public static PromotionResult NoDiscount()
    {
        return new PromotionResult(0, null, null);
    }

    /// <summary>
    ///     Creates a result with a discount.
    /// </summary>
    public static PromotionResult WithDiscount(
        decimal discountAmount,
        string promotionName,
        string? couponCode = null)
    {
        return new PromotionResult(discountAmount, promotionName, couponCode);
    }
}
