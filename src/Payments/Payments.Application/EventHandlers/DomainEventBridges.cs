using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Wolverine handler for PaymentCompletedDomainEvent.
///     Bridges domain events to integration events via cascading messages.
///     Pattern: Domain event handler returns integration event as cascading message.
///
///     WEBHOOK-FIRST PATTERN:
///     This is triggered by ProcessExternalPaymentConfirmation handler after webhook confirmation.
///     Returns PaymentSucceeded to continue the saga.
/// </summary>
[WolverineHandler]
public static class PaymentCompletedDomainEventHandler
{
    /// <summary>
    ///     Handles the domain event and returns PaymentSucceeded as a cascading message.
    ///     This triggers saga continuation after webhook confirmation.
    /// </summary>
    public static PaymentSucceeded Handle(
        PaymentCompletedDomainEvent domainEvent,
        ILogger<PaymentCompletedDomainEvent> logger)
    {
        logger.LogInformation(
            "Bridging PaymentCompletedDomainEvent to PaymentSucceeded for TransactionId: {TransactionId}, OrderId: {OrderId}. " +
            "This event came from webhook confirmation.",
            domainEvent.ExternalTransactionId,
            domainEvent.OrderId);

        // Return PaymentSucceeded event as cascading message
        // Wolverine handles publishing via the transactional outbox
        return new PaymentSucceeded(
            domainEvent.OrderId,
            domainEvent.ExternalTransactionId,
            domainEvent.Amount);
    }
}
