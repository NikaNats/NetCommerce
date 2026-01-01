using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Ordering.Application.EventHandlers;

/// <summary>
///     Integration event handler for PaymentCompletedIntegrationEvent.
///     When a payment is completed in the Payments module,
///     this handler marks the order as paid in the Ordering module.
///     This bridges the Payments and Ordering modules without direct coupling.
///     Architecture: Payments -> [Integration Event] -> Ordering
/// </summary>
public sealed class PaymentCompletedIntegrationEventHandler : INotificationHandler<PaymentCompletedIntegrationEvent>
{
    private readonly ILogger<PaymentCompletedIntegrationEventHandler> _logger;
    private readonly ISender _mediator;

    public PaymentCompletedIntegrationEventHandler(ISender mediator,
        ILogger<PaymentCompletedIntegrationEventHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(PaymentCompletedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing PaymentCompletedIntegrationEvent for TransactionId: {TransactionId}, OrderId: {OrderId}",
                notification.TransactionId,
                notification.OrderId);

            // Send command to mark order as paid and persist payment transaction id
            var command = new ConfirmOrderCommand(notification.OrderId, notification.TransactionId);
            var result = await _mediator.Send(command, cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError(
                    "Failed to confirm order {OrderId}: {Error}",
                    notification.OrderId,
                    result.Error?.Description);
                throw new InvalidOperationException($"Failed to confirm order: {result.Error?.Description}");
            }

            _logger.LogInformation(
                "Successfully processed PaymentCompletedIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling PaymentCompletedIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}