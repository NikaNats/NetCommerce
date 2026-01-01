using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

namespace NetCommerce.Ordering.Infrastructure.Outbox;

public sealed class OrderingOutboxDeadLetterHandler : IOutboxDeadLetterHandler<Persistence.OrderingDbContext>
{
    private const string InventoryConfirmationFailedRefundReason = "inventory_confirmation_failed";

    private readonly IMediator _mediator;
    private readonly ILogger<OrderingOutboxDeadLetterHandler> _logger;

    public OrderingOutboxDeadLetterHandler(
        IMediator mediator,
        ILogger<OrderingOutboxDeadLetterHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task HandleAsync(
        OutboxMessage message,
        IDomainEvent? domainEvent,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // We only compensate for the known dangerous gap:
        // Order is already Paid, but inventory confirmation cannot complete and has hit max retries.
        if (domainEvent is not OrderPaidDomainEvent orderPaid)
        {
            _logger.LogCritical(
                exception,
                "Outbox message {MessageId} permanently failed (type: {Type}). No compensation configured.",
                message.Id,
                message.Type);

            return;
        }

        _logger.LogCritical(
            exception,
            "OrderPaidDomainEvent permanently failed after max retries. Triggering compensation. OrderId: {OrderId}, PaymentTransactionId: {PaymentTransactionId}",
            orderPaid.OrderId,
            orderPaid.PaymentTransactionId);

        // Publish an integration event (no direct Payments dependency).
        await _mediator.Publish(
            new OrderInventoryConfirmationFailedIntegrationEvent(
                orderPaid.OrderId,
                orderPaid.PaymentTransactionId,
                orderPaid.TotalAmount,
                InventoryConfirmationFailedRefundReason,
                message.Error),
            cancellationToken);

        // Cancel the order to stop downstream processing. Refund is handled asynchronously by Payments.
        var cancelResult = await _mediator.Send(
            new NetCommerce.Ordering.Application.Orders.Commands.CancelOrderCommand(
                orderPaid.OrderId,
                "Inventory confirmation failed after payment; refund requested"),
            cancellationToken);

        if (!cancelResult.IsSuccess)
        {
            _logger.LogCritical(
                "Failed to cancel order after dead-lettered OrderPaidDomainEvent. OrderId: {OrderId}, Error: {Error}",
                orderPaid.OrderId,
                cancelResult.Error?.Description);
        }
    }
}
