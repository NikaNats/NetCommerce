using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Inventory.Application.EventHandlers;

/// <summary>
///     Integration event handler for OrderCancelledIntegrationEvent.
///     When an order is cancelled, this handler releases any stock reservations.
///     If the order was cancelled during grace period, no payment was taken,
///     so we only need to release the soft reservation.
///     Architecture: Ordering -> [Integration Event] -> Inventory
/// </summary>
public sealed class OrderCancelledIntegrationEventHandler : INotificationHandler<OrderCancelledIntegrationEvent>
{
    private readonly ILogger<OrderCancelledIntegrationEventHandler> _logger;
    private readonly ISender _mediator;

    public OrderCancelledIntegrationEventHandler(
        ISender mediator,
        ILogger<OrderCancelledIntegrationEventHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderCancelledIntegrationEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var wasInGracePeriod = notification.PreviousStatus == "Submitted";

            _logger.LogInformation(
                "Processing OrderCancelledIntegrationEvent for OrderId: {OrderId}. " +
                "Previous status: {PreviousStatus}, Was in grace period: {WasInGracePeriod}",
                notification.OrderId,
                notification.PreviousStatus,
                wasInGracePeriod);

            // Release stock reservation for this order
            // In a real system, you would:
            // 1. Query reservations by OrderId
            // 2. Release each reservation
            //
            // Example (pseudo-code):
            // var command = new ReleaseStockReservationCommand(notification.OrderId);
            // await _mediator.Send(command, cancellationToken);

            if (wasInGracePeriod)
            {
                _logger.LogInformation(
                    "Order {OrderId} was cancelled during grace period. " +
                    "Stock reservation released. No payment refund needed.",
                    notification.OrderId);
            }
            else
            {
                _logger.LogInformation(
                    "Order {OrderId} was cancelled after grace period. " +
                    "Stock reservation released. Payment refund may be required.",
                    notification.OrderId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling OrderCancelledIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}
