#nullable enable
namespace NetCommerce.Ordering.Infrastructure.Services;

/// <summary>
///     Configurable coupon table backing <see cref="SimplePromotionEngine"/>.
///     Bind from the <c>Ordering:Promotions</c> section
///     (e.g. <c>Ordering__Promotions__Coupons__WELCOME10=0.10</c>).
///     Active coupons are real money; Production-like environments must
///     explicitly acknowledge the table (see validator).
/// </summary>
public sealed class PromotionOptions
{
    public const string SectionName = "Ordering:Promotions";

    /// <summary>
    ///     Coupon code → discount fraction (0.10 = 10% off the line total).
    /// </summary>
    public Dictionary<string, decimal> Coupons { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WELCOME10"] = 0.10m,
        ["SAVE20"] = 0.20m,
        ["SUMMER15"] = 0.15m,
        ["FIRSTORDER"] = 0.25m
    };

    /// <summary>
    ///     Coupon code → customer-facing promotion name.
    /// </summary>
    public Dictionary<string, string> CouponNames { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WELCOME10"] = "Welcome 10% Off",
        ["SAVE20"] = "Save 20%",
        ["SUMMER15"] = "Summer Sale 15%",
        ["FIRSTORDER"] = "First Order 25% Off"
    };

    /// <summary>
    ///     Explicit acknowledgment that the active coupon table has been reviewed
    ///     by marketing/finance for Production use.
    /// </summary>
    public bool AcknowledgedInProduction { get; set; } = false;
}
