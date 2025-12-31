using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Inventory.Application.EventHandlers;

/// <summary>
/// Integration event handler for OrderPaidIntegrationEvent.
/// 
/// When an order is marked as paid in the Ordering module,
/// this handler confirms stock reservations in the Inventory module.
/// 
/// This bridges the Ordering and Inventory modules without direct coupling.
/// Architecture: Ordering -> [Integration Event] -> Inventory
/// </summary>
public sealed class OrderPaidIntegrationEventHandler : INotificationHandler<OrderPaidIntegrationEvent>
{
    private readonly ISender _mediator;
    private readonly ILogger<OrderPaidIntegrationEventHandler> _logger;

    public OrderPaidIntegrationEventHandler(ISender mediator, ILogger<OrderPaidIntegrationEventHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderPaidIntegrationEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing OrderPaidIntegrationEvent for OrderId: {OrderId}, OrderNumber: {OrderNumber}",
                notification.OrderId,
                notification.OrderNumber);

            // Note: In a real system, you would:
            // 1. Query the Ordering module (via SharedKernel service) to get order line items
            // 2. For each line item, confirm the corresponding stock reservation
            // 
            // For now, this is a placeholder that shows the pattern.
            // The actual order items would contain ProductId and ReservationId.
            //
            // Example (pseudo-code):
            // var orderItems = await _orderQueryService.GetOrderItemsAsync(notification.OrderId);
            // foreach (var item in orderItems)
            // {
            //     var command = new ConfirmReservationCommand(item.ProductId, item.ReservationId);
            //     await _mediator.Send(command, cancellationToken);
            // }

            _logger.LogInformation(
                "Successfully processed OrderPaidIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling OrderPaidIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}
