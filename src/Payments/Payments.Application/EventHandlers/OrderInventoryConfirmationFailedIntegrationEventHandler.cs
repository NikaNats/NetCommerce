using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Domain.Shared.Events;
using Wolverine.Attributes;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Wolverine handler for OrderInventoryConfirmationFailedIntegrationEvent.
///     Compensating action: when inventory confirmation fails after payment,
///     this handler returns a refund command as a cascading message.
/// </summary>
[WolverineHandler]
public static class OrderInventoryConfirmationFailedHandler
{
    /// <summary>
    ///     Handles inventory confirmation failure by returning a refund command.
    ///     Wolverine will execute the refund command as a cascading message.
    /// </summary>
    public static RefundPaymentTransactionCommand Handle(
        OrderInventoryConfirmationFailedIntegrationEvent integrationEvent,
        ILogger<OrderInventoryConfirmationFailedIntegrationEvent> logger)
    {
        logger.LogCritical(
            "Received OrderInventoryConfirmationFailedIntegrationEvent. Initiating refund. " +
            "OrderId: {OrderId}, PaymentTransactionId: {PaymentTransactionId}",
            integrationEvent.OrderId,
            integrationEvent.PaymentTransactionId);

        // Return refund command as cascading message
        // Wolverine will execute this command after this handler completes
        return new RefundPaymentTransactionCommand(
            integrationEvent.PaymentTransactionId,
            integrationEvent.Amount,
            integrationEvent.FailureReason);
    }
}
