using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Wolverine handler for PaymentCompletedDomainEvent.
///     Bridges domain events to integration events via cascading messages.
///     Pattern: Domain event handler returns integration event as cascading message.
/// </summary>
[WolverineHandler]
public static class PaymentCompletedDomainEventHandler
{
    /// <summary>
    ///     Handles the domain event and returns an integration event as a cascading message.
    ///     Wolverine will automatically publish the returned message through the outbox.
    /// </summary>
    public static PaymentCompletedIntegrationEvent Handle(
        PaymentCompletedDomainEvent domainEvent,
        ILogger<PaymentCompletedDomainEvent> logger)
    {
        logger.LogInformation(
            "Bridging PaymentCompletedDomainEvent to PaymentCompletedIntegrationEvent for TransactionId: {TransactionId}, OrderId: {OrderId}",
            domainEvent.ExternalTransactionId,
            domainEvent.OrderId);

        // Return integration event as cascading message
        // Wolverine handles publishing via the transactional outbox
        return new PaymentCompletedIntegrationEvent(
            domainEvent.ExternalTransactionId,
            domainEvent.OrderId,
            domainEvent.Amount);
    }
}
