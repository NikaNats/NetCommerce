using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Wolverine handler for OrderGracePeriodConfirmedIntegrationEvent.
///     When the grace period ends, this handler initiates payment capture.
///     Architecture: Ordering -> [Integration Event] -> Payments
/// </summary>
[WolverineHandler]
public static class OrderGracePeriodConfirmedHandler
{
    /// <summary>
    ///     Handles the grace period confirmation and initiates payment processing.
    ///     This is the key benefit of the grace period pattern - payment is only
    ///     processed AFTER the user has had time to cancel without fees.
    /// </summary>
    public static void Handle(
        OrderGracePeriodConfirmedIntegrationEvent integrationEvent,
        ILogger<OrderGracePeriodConfirmedIntegrationEvent> logger)
    {
        logger.LogInformation(
            "Processing OrderGracePeriodConfirmedIntegrationEvent for OrderId: {OrderId}, " +
            "OrderNumber: {OrderNumber}, Amount: {Amount} {Currency}. " +
            "Grace period has ended - initiating payment capture.",
            integrationEvent.OrderId,
            integrationEvent.OrderNumber,
            integrationEvent.TotalAmount.Amount,
            integrationEvent.TotalAmount.Currency);

        // NOW we process the payment
        // If the user cancelled during the grace period, this event never fires
        //
        // In a real system, you would:
        // 1. Get the payment method stored for the order/customer
        // 2. Call the payment gateway to capture the payment
        // 3. On success, return a CapturePaymentCommand as cascading message
        //
        // Example:
        // return new CapturePaymentCommand(
        //     integrationEvent.OrderId, 
        //     integrationEvent.CustomerId,
        //     integrationEvent.TotalAmount);

        logger.LogInformation(
            "Payment processing initiated for OrderId: {OrderId}",
            integrationEvent.OrderId);
    }
}