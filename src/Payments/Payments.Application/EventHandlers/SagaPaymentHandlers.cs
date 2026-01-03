using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Payments.Application.EventHandlers;

/// <summary>
///     Handlers for Saga commands in the Payments module.
///     These handlers process payment requests from the OrderFulfillmentSaga.
/// </summary>
[WolverineHandler]
public static class SagaPaymentHandlers
{
    /// <summary>
    ///     Handles payment request from the OrderFulfillmentSaga.
    ///     In a real implementation, this would call a payment gateway.
    ///     Returns a PaymentSucceeded or PaymentFailed event as cascading message.
    /// </summary>
    public static async Task<object> Handle(
        RequestPaymentCommand command,
        ILogger<RequestPaymentCommand> logger)
    {
        logger.LogInformation(
            "Processing payment request for Order {OrderId} ({OrderNumber}). Amount: {Amount}",
            command.OrderId,
            command.OrderNumber,
            command.Amount);

        try
        {
            // Simulate payment gateway call
            // In production: Call Stripe, PayPal, etc.
            await Task.Delay(100); // Simulated latency

            // Simulate success (in production, check gateway response)
            var transactionId = Guid.NewGuid();

            logger.LogInformation(
                "Payment successful for Order {OrderId}. TransactionId: {TransactionId}",
                command.OrderId,
                transactionId);

            // Return success event as cascading message
            // Wolverine will route this back to the Saga
            return new PaymentSucceeded(
                command.OrderId,
                transactionId,
                command.Amount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Payment failed for Order {OrderId}. Error: {Error}",
                command.OrderId,
                ex.Message);

            // Return failure event
            return new PaymentFailed(
                command.OrderId,
                ex.Message,
                "GATEWAY_ERROR");
        }
    }

    /// <summary>
    ///     Handles refund request from the OrderFulfillmentSaga.
    ///     This is a compensating action when inventory confirmation fails.
    /// </summary>
    public static async Task<object> Handle(
        RefundPaymentCommand command,
        ILogger<RefundPaymentCommand> logger)
    {
        logger.LogWarning(
            "Processing refund for Order {OrderId}. TransactionId: {TransactionId}, " +
            "Amount: {Amount}. Reason: {Reason}",
            command.OrderId,
            command.PaymentTransactionId,
            command.Amount,
            command.Reason);

        try
        {
            // Simulate refund processing
            // In production: Call payment gateway's refund API
            await Task.Delay(100);

            var refundTransactionId = Guid.NewGuid();

            logger.LogInformation(
                "Refund successful for Order {OrderId}. RefundTransactionId: {RefundTransactionId}",
                command.OrderId,
                refundTransactionId);

            return new RefundCompleted(
                command.OrderId,
                refundTransactionId,
                command.Amount);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "CRITICAL: Refund failed for Order {OrderId}. " +
                "Customer may have been charged without service. Manual intervention required!",
                command.OrderId);

            return new RefundFailed(command.OrderId, ex.Message);
        }
    }
}
