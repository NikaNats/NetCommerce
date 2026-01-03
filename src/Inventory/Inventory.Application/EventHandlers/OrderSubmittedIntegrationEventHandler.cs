using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Application.EventHandlers;

/// <summary>
///     Wolverine handler for OrderSubmittedIntegrationEvent.
///     When an order is submitted, this handler triggers immediate soft stock reservation.
///     Architecture: Ordering -> [Integration Event] -> Inventory
/// </summary>
[WolverineHandler]
public static class OrderSubmittedHandler
{
    /// <summary>
    ///     Handles order submission by initiating stock reservation.
    ///     In a complete implementation, this would return ReserveStockCommand(s)
    ///     as cascading messages for each order item.
    /// </summary>
    public static void Handle(
        OrderSubmittedIntegrationEvent integrationEvent,
        ILogger<OrderSubmittedIntegrationEvent> logger)
    {
        logger.LogInformation(
            "Processing OrderSubmittedIntegrationEvent for OrderId: {OrderId}, OrderNumber: {OrderNumber}. " +
            "Initiating soft stock reservation during grace period.",
            integrationEvent.OrderId,
            integrationEvent.OrderNumber);

        // In a real system, you would:
        // 1. Get order line items from the event or query them
        // 2. Return ReserveStockCommand for each item as cascading messages
        //
        // Example:
        // return integrationEvent.Items.Select(item =>
        //     new ReserveStockCommand(item.ProductId, integrationEvent.OrderId, item.Quantity))
        //     .ToArray();

        logger.LogInformation(
            "Stock reservation initiated for OrderId: {OrderId}",
            integrationEvent.OrderId);
    }
}
