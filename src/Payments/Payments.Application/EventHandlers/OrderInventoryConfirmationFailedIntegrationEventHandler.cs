using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Compensating action handler: when inventory confirmation cannot be completed after payment,
///     attempt to refund the captured payment.
/// </summary>
public sealed class OrderInventoryConfirmationFailedIntegrationEventHandler
    : INotificationHandler<OrderInventoryConfirmationFailedIntegrationEvent>
{
    private readonly ILogger<OrderInventoryConfirmationFailedIntegrationEventHandler> _logger;
    private readonly ISender _sender;

    public OrderInventoryConfirmationFailedIntegrationEventHandler(
        ISender sender,
        ILogger<OrderInventoryConfirmationFailedIntegrationEventHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(OrderInventoryConfirmationFailedIntegrationEvent notification,
        CancellationToken cancellationToken)
    {
        _logger.LogCritical(
            "Received OrderInventoryConfirmationFailedIntegrationEvent. Attempting refund. OrderId: {OrderId}, PaymentTransactionId: {PaymentTransactionId}",
            notification.OrderId,
            notification.PaymentTransactionId);

        var refundResult = await _sender.Send(
            new RefundPaymentTransactionCommand(
                notification.PaymentTransactionId,
                notification.Amount,
                notification.FailureReason),
            cancellationToken);

        if (!refundResult.IsSuccess)
            _logger.LogCritical(
                "Refund failed after inventory confirmation failure. OrderId: {OrderId}, PaymentTransactionId: {PaymentTransactionId}, Error: {Error}, Details: {Details}",
                notification.OrderId,
                notification.PaymentTransactionId,
                refundResult.Error?.Description,
                notification.FailureDetails);
        else
            _logger.LogInformation(
                "Refund succeeded after inventory confirmation failure. OrderId: {OrderId}, PaymentTransactionId: {PaymentTransactionId}",
                notification.OrderId,
                notification.PaymentTransactionId);
    }
}