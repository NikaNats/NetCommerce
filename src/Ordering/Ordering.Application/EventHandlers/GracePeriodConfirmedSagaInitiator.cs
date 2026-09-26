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
    ///     Loads the real order items so inventory reservation operates on actual data.
    /// </summary>
    public static async Task<StartOrderFulfillmentCommand?> Handle(
        OrderGracePeriodConfirmedIntegrationEvent @event,
        IOrderRepository orderRepository,
        ILogger<OrderGracePeriodConfirmedIntegrationEvent> logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Grace period confirmed for Order {OrderId} ({OrderNumber}). " +
            "Initiating OrderFulfillmentSaga.",
            @event.OrderId,
            @event.OrderNumber);

        var order = await orderRepository.GetByIdAsync(@event.OrderId, cancellationToken);
        if (order is null)
        {
            logger.LogWarning(
                "Grace period confirmed for unknown Order {OrderId}. Saga will not start.",
                @event.OrderId);
            return null;
        }

        var items = order.Items
            .Select(i => new OrderItemReservation(i.ProductId, i.Quantity, i.Sku))
            .ToList();

        if (items.Count == 0)
        {
            logger.LogWarning(
                "Order {OrderId} has no items. Saga will start and fail fast via inventory reservation.",
                @event.OrderId);
        }

        // Order aggregate does not persist the client payment-method token,
        // so the saga starts without one; the payment handler falls back to
        // a dev mock token when PaymentMethodId is empty.
        return new StartOrderFulfillmentCommand(
            @event.OrderId,
            @event.CustomerId,
            @event.OrderNumber,
            @event.TotalAmount,
            items);
    }
}
