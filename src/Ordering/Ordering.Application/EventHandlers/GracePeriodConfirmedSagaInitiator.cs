using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Domain.Shared.Events;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Application.EventHandlers;

/// <summary>
///     Initiates the OrderFulfillmentSaga when the grace period ends.
/// </summary>
[WolverineHandler]
public static class GracePeriodConfirmedSagaInitiator
{
    /// <summary>
    ///     Starts the OrderFulfillmentSaga when an order's grace period is confirmed.
    ///     This bridges the domain event to the saga workflow.
    /// </summary>
    public static StartOrderFulfillmentCommand? Handle(
        OrderGracePeriodConfirmedIntegrationEvent @event,
        IOrderRepository orderRepository,
        ILogger<OrderGracePeriodConfirmedIntegrationEvent> logger)
    {
        logger.LogInformation(
            "Grace period confirmed for Order {OrderId} ({OrderNumber}). " +
            "Initiating OrderFulfillmentSaga.",
            @event.OrderId,
            @event.OrderNumber);

        // In a real implementation, we would fetch the order items
        // For now, we'll create a placeholder - in production this data
        // should come from the domain event or be fetched from the repository
        var items = new List<OrderItemReservation>
        {
            // These would come from the actual order
            // Example placeholder:
            // new OrderItemReservation(productId, quantity, sku)
        };

        // Start the saga by returning the initiation command
        return new StartOrderFulfillmentCommand(
            @event.OrderId,
            @event.CustomerId,
            @event.OrderNumber,
            @event.TotalAmount,
            items);
    }
}
