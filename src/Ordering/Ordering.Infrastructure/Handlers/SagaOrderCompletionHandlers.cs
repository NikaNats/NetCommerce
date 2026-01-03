using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Infrastructure.Handlers;

/// <summary>
///     Handlers for completing or failing orders based on saga outcomes.
///     Note: Wolverine's AutoApplyTransactions policy handles SaveChanges automatically.
/// </summary>
[WolverineHandler]
public static class SagaOrderCompletionHandlers
{
    /// <summary>
    ///     Handles successful order fulfillment.
    ///     Marks the order as paid in the domain.
    /// </summary>
    public static async Task Handle(
        FinalizeOrderCommand command,
        OrderingDbContext dbContext,
        ILogger<FinalizeOrderCommand> logger)
    {
        logger.LogInformation(
            "Finalizing order {OrderId}. PaymentTransactionId: {TransactionId}",
            command.OrderId,
            command.PaymentTransactionId);

        var order = await dbContext.Orders.FindAsync(command.OrderId);

        if (order is null)
        {
            logger.LogWarning(
                "Order {OrderId} not found for finalization. May have been deleted.",
                command.OrderId);
            return;
        }

        // Update order status to reflect successful payment and inventory confirmation
        try
        {
            // First confirm stock if not already done
            if (order.Status == OrderStatus.AwaitingValidation)
            {
                order.ConfirmStock();
            }

            // Then mark as paid
            if (order.Status == OrderStatus.StockConfirmed)
            {
                order.MarkAsPaid(command.PaymentTransactionId);
            }

            // Note: Wolverine handles SaveChangesAsync automatically via AutoApplyTransactions

            logger.LogInformation(
                "Order {OrderId} finalized successfully. Status: {Status}",
                command.OrderId,
                order.Status);
        }
        catch (InvalidOperationException ex)
        {
            // Order may already be in correct state (idempotency)
            logger.LogWarning(
                "Could not transition order {OrderId} state: {Error}. Current status: {Status}",
                command.OrderId,
                ex.Message,
                order.Status);
        }
    }

    /// <summary>
    ///     Handles order failure from the saga.
    ///     Cancels the order with the failure reason.
    /// </summary>
    public static async Task Handle(
        FailOrderCommand command,
        OrderingDbContext dbContext,
        ILogger<FailOrderCommand> logger)
    {
        logger.LogWarning(
            "Failing order {OrderId}. Reason: {Reason}",
            command.OrderId,
            command.FailureReason);

        var order = await dbContext.Orders.FindAsync(command.OrderId);

        if (order is null)
        {
            logger.LogWarning(
                "Order {OrderId} not found for failure processing. May have been deleted.",
                command.OrderId);
            return;
        }

        try
        {
            // Cancel the order with the failure reason
            if (order.Status != OrderStatus.Cancelled)
            {
                order.Cancel(command.FailureReason);

                // Note: Wolverine handles SaveChangesAsync automatically via AutoApplyTransactions

                logger.LogInformation(
                    "Order {OrderId} cancelled due to saga failure. Reason: {Reason}",
                    command.OrderId,
                    command.FailureReason);
            }
            else
            {
                logger.LogInformation(
                    "Order {OrderId} already cancelled. Skipping.",
                    command.OrderId);
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                "Could not cancel order {OrderId}: {Error}. Current status: {Status}",
                command.OrderId,
                ex.Message,
                order.Status);
        }
    }
}
