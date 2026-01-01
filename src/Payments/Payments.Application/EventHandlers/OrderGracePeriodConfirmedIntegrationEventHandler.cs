using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Integration event handler for OrderGracePeriodConfirmedIntegrationEvent.
///     When the grace period ends, this handler initiates payment capture.
///     This is the key benefit of the grace period pattern - payment is only
///     processed AFTER the user has had time to cancel without fees.
///     Architecture: Ordering -> [Integration Event] -> Payments
/// </summary>
public sealed class OrderGracePeriodConfirmedIntegrationEventHandler 
    : INotificationHandler<OrderGracePeriodConfirmedIntegrationEvent>
{
    private readonly ILogger<OrderGracePeriodConfirmedIntegrationEventHandler> _logger;
    private readonly ISender _mediator;

    public OrderGracePeriodConfirmedIntegrationEventHandler(
        ISender mediator,
        ILogger<OrderGracePeriodConfirmedIntegrationEventHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(
        OrderGracePeriodConfirmedIntegrationEvent notification, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing OrderGracePeriodConfirmedIntegrationEvent for OrderId: {OrderId}, " +
                "OrderNumber: {OrderNumber}, Amount: {Amount} {Currency}. " +
                "Grace period has ended - initiating payment capture.",
                notification.OrderId,
                notification.OrderNumber,
                notification.TotalAmount.Amount,
                notification.TotalAmount.Currency);

            // NOW we process the payment
            // If the user cancelled during the 5 minutes, this event never fires
            // Money is saved!
            //
            // In a real system, you would:
            // 1. Get the payment method stored for the order/customer
            // 2. Call the payment gateway to capture the payment
            // 3. On success, send a command to mark the order as paid
            //
            // Example (pseudo-code):
            // var command = new CapturePaymentCommand(
            //     notification.OrderId, 
            //     notification.CustomerId,
            //     notification.TotalAmount);
            // var result = await _mediator.Send(command, cancellationToken);
            //
            // if (result.IsSuccess)
            // {
            //     // Publish PaymentCompletedIntegrationEvent
            // }

            _logger.LogInformation(
                "Payment processing initiated for OrderId: {OrderId}",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling OrderGracePeriodConfirmedIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}
