using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Application.EventHandlers;

/// <summary>
///     Wolverine handler for OrderPaidIntegrationEvent.
///     When an order is marked as paid, this handler confirms stock reservations.
///     Architecture: Ordering -> [Integration Event] -> Inventory
/// </summary>
[WolverineHandler]
public static class OrderPaidHandler
{
    /// <summary>
    ///     Handles order payment confirmation by confirming stock reservations.
    ///     In a complete implementation, this would return ConfirmReservationCommand(s)
    ///     as cascading messages for each order item.
    /// </summary>
    public static void Handle(
        OrderPaidIntegrationEvent integrationEvent,
        ILogger<OrderPaidIntegrationEvent> logger)
    {
        logger.LogInformation(
            "Processing OrderPaidIntegrationEvent for OrderId: {OrderId}, OrderNumber: {OrderNumber}",
            integrationEvent.OrderId,
            integrationEvent.OrderNumber);

        // In a real system, you would:
        // 1. Get order items with reservation IDs from the event
        // 2. Return ConfirmReservationCommand for each item as cascading messages
        //
        // Example:
        // return integrationEvent.Items.Select(item =>
        //     new ConfirmReservationCommand(item.ProductId, item.ReservationId))
        //     .ToArray();

        logger.LogInformation(
            "Stock reservation confirmation initiated for OrderId: {OrderId}",
            integrationEvent.OrderId);
    }
}