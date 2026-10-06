namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Named cancellation rules for <see cref="Order"/>.
///     Extracted from repeated status branches so the ubiquitous language
///     ("grace-period cancel is free, post-payment cancel needs refund")
///     lives in one place instead of handlers.
/// </summary>
public static class OrderCancellationPolicy
{
    /// <summary>Whether <see cref="Order.Cancel"/> may run from this status.</summary>
    public static bool CanCancel(OrderStatus status) =>
        status is not OrderStatus.Delivered and not OrderStatus.Cancelled;

    /// <summary>
    ///     Whether cancelling from <paramref name="previousStatus"/> requires
    ///     refund/compensation (payment was or may have been taken).
    /// </summary>
    public static bool RequiresRefund(OrderStatus previousStatus) =>
        previousStatus is not OrderStatus.Submitted;

    /// <summary>Whether the cancel happened inside the free grace-period window.</summary>
    public static bool IsGracePeriodCancel(OrderStatus previousStatus) =>
        previousStatus == OrderStatus.Submitted;
}
