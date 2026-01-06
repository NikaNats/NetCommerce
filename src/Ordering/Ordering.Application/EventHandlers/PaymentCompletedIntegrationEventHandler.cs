using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Domain.Shared.Events;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Application.EventHandlers;

/// <summary>
///     Wolverine handler for PaymentCompletedIntegrationEvent.
///     When a payment is completed, this handler returns a command to confirm the order.
///     Architecture: Payments -> [Integration Event] -> Ordering
/// </summary>
[WolverineHandler]
public static class PaymentCompletedHandler
{
    /// <summary>
    ///     Handles payment completion by returning a ConfirmOrderCommand as cascading message.
    ///     Wolverine will execute the command to mark the order as paid.
    /// </summary>
    public static ConfirmOrderCommand Handle(
        PaymentCompletedIntegrationEvent integrationEvent,
        ILogger<PaymentCompletedIntegrationEvent> logger)
    {
        logger.LogInformation(
            "Processing PaymentCompletedIntegrationEvent for TransactionId: {TransactionId}, OrderId: {OrderId}",
            integrationEvent.ExternalTransactionId,
            integrationEvent.OrderId);

        // Return command as cascading message
        // Wolverine will execute this to confirm the order
        return new ConfirmOrderCommand(integrationEvent.OrderId, integrationEvent.ExternalTransactionId);
    }
}
