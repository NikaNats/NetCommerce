using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Application.EventHandlers;

/// <summary>
///     Wolverine handler for OrderCancelledIntegrationEvent.
///     When an order is cancelled, this handler releases stock reservations.
///     Architecture: Ordering -> [Integration Event] -> Inventory
/// </summary>
[WolverineHandler]
public static class OrderCancelledHandler
{
    /// <summary>
    ///     Handles order cancellation by releasing stock reservations.
    ///     In a complete implementation, this would return ReleaseReservationCommand(s)
    ///     as cascading messages for each order item.
    /// </summary>
    public static void Handle(
        OrderCancelledIntegrationEvent integrationEvent,
        ILogger<OrderCancelledIntegrationEvent> logger)
    {
        var wasInGracePeriod = integrationEvent.PreviousStatus == "Submitted";

        logger.LogInformation(
            "Processing OrderCancelledIntegrationEvent for OrderId: {OrderId}. " +
            "Previous status: {PreviousStatus}, Was in grace period: {WasInGracePeriod}",
            integrationEvent.OrderId,
            integrationEvent.PreviousStatus,
            wasInGracePeriod);

        // In a real system, you would:
        // 1. Get order items with reservation IDs from the event
        // 2. Return ReleaseReservationCommand for each item as cascading messages
        //
        // Example:
        // return integrationEvent.Items.Select(item =>
        //     new ReleaseReservationCommand(item.ProductId, item.ReservationId))
        //     .ToArray();

        if (wasInGracePeriod)
            logger.LogInformation(
                "Order {OrderId} was cancelled during grace period. " +
                "Stock reservation released. No payment refund needed.",
                integrationEvent.OrderId);
        else
            logger.LogInformation(
                "Order {OrderId} was cancelled after grace period. " +
                "Stock reservation released. Payment refund may be required.",
                integrationEvent.OrderId);
    }
}
