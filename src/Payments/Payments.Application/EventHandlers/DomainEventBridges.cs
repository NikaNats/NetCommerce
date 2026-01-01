using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Domain event to integration event bridge for PaymentCompletedDomainEvent.
///     This handler listens to domain events within the Payments module
///     and converts them to integration events that Ordering module can subscribe to.
///     Pattern: Payments Module publishes PaymentCompletedDomainEvent internally
///     -> Bridge converts to PaymentCompletedIntegrationEvent
///     -> Ordering Module subscribers receive PaymentCompletedIntegrationEvent
/// </summary>
public sealed class PaymentCompletedDomainEventToBridgeHandler : INotificationHandler<PaymentCompletedDomainEvent>
{
    private readonly ILogger<PaymentCompletedDomainEventToBridgeHandler> _logger;
    private readonly IMediator _mediator;

    public PaymentCompletedDomainEventToBridgeHandler(IMediator mediator,
        ILogger<PaymentCompletedDomainEventToBridgeHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(PaymentCompletedDomainEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Bridging PaymentCompletedDomainEvent to PaymentCompletedIntegrationEvent for TransactionId: {TransactionId}, OrderId: {OrderId}",
                notification.TransactionId,
                notification.OrderId);

            // Convert domain event to integration event and publish
            var integrationEvent = new PaymentCompletedIntegrationEvent(
                notification.TransactionId,
                notification.OrderId,
                notification.Amount);

            await _mediator.Publish(integrationEvent, cancellationToken);

            _logger.LogInformation(
                "PaymentCompletedIntegrationEvent published for OrderId: {OrderId}",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error bridging PaymentCompletedDomainEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}