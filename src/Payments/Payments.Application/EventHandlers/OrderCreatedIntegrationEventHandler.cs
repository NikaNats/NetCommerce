using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Integration event handler for OrderCreatedIntegrationEvent.
///     When an order is created in the Ordering module,
///     this handler prepares payment processing setup.
///     This bridges the Ordering and Payments modules without direct coupling.
///     Architecture: Ordering -> [Integration Event] -> Payments
///     Note: The actual payment gateway integration happens when:
///     1. User submits payment at the API
///     2. ProcessPaymentCommand is sent to Payments module
///     3. Payment transaction is created and sent to payment gateway
/// </summary>
public sealed class OrderCreatedIntegrationEventHandler : INotificationHandler<OrderCreatedIntegrationEvent>
{
    private readonly ILogger<OrderCreatedIntegrationEventHandler> _logger;

    public OrderCreatedIntegrationEventHandler(ILogger<OrderCreatedIntegrationEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderCreatedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Received OrderCreatedIntegrationEvent for OrderId: {OrderId}, OrderNumber: {OrderNumber}, CustomerId: {CustomerId}",
                notification.OrderId,
                notification.OrderNumber,
                notification.CustomerId);

            // In a real system, you might:
            // 1. Cache the order metadata for quick payment lookup
            // 2. Send notification to customer (email, SMS)
            // 3. Prepare fraud detection rules
            // 4. Initialize payment risk assessment
            //
            // For now, just log the event to demonstrate the bridge.

            _logger.LogInformation(
                "OrderCreatedIntegrationEvent processed successfully for OrderId: {OrderId}",
                notification.OrderId);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling OrderCreatedIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}