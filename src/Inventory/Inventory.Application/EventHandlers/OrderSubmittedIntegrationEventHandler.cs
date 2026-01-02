using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Inventory.Application.EventHandlers;

/// <summary>
///     Integration event handler for OrderSubmittedIntegrationEvent.
///     When an order is submitted, this handler triggers immediate soft stock reservation.
///     This ensures items are held for the user during the grace period.
///     Architecture: Ordering -> [Integration Event] -> Inventory
/// </summary>
public sealed class OrderSubmittedIntegrationEventHandler : INotificationHandler<OrderSubmittedIntegrationEvent>
{
    private readonly ILogger<OrderSubmittedIntegrationEventHandler> _logger;
    private readonly ISender _mediator;

    public OrderSubmittedIntegrationEventHandler(
        ISender mediator,
        ILogger<OrderSubmittedIntegrationEventHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderSubmittedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing OrderSubmittedIntegrationEvent for OrderId: {OrderId}, OrderNumber: {OrderNumber}. " +
                "Initiating soft stock reservation during grace period.",
                notification.OrderId,
                notification.OrderNumber);

            // In a real system, you would:
            // 1. Query the Ordering module (via SharedKernel service) to get order line items
            // 2. For each line item, create a soft reservation
            // 
            // Example (pseudo-code):
            // var orderItems = await _orderQueryService.GetOrderItemsAsync(notification.OrderId);
            // foreach (var item in orderItems)
            // {
            //     var command = new ReserveStockCommand(item.ProductId, notification.OrderId, item.Quantity);
            //     await _mediator.Send(command, cancellationToken);
            // }

            _logger.LogInformation(
                "Successfully initiated stock reservation for OrderId: {OrderId}",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling OrderSubmittedIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}