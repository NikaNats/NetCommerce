using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Gateways;
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
    ///     Uses the injected payment gateway to process payments.
    ///     Returns a PaymentSucceeded or PaymentFailed event as cascading message.
    /// </summary>
    public static async Task<object> Handle(
        RequestPaymentCommand command,
        IPaymentGateway paymentGateway,
        ILogger<RequestPaymentCommand> logger)
    {
        logger.LogInformation(
            "Processing payment request for Order {OrderId} ({OrderNumber}). Amount: {Amount}",
            command.OrderId,
            command.OrderNumber,
            command.Amount);

        try
        {
            // Create payment request for the gateway
            var paymentRequest = new PaymentRequest(
                OrderId: command.OrderId,
                Amount: command.Amount,
                PaymentMethodToken: "default_token", // Simulated token
                IdempotencyKey: $"order_{command.OrderId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Description: $"Payment for order {command.OrderNumber}");

            // Process payment through the gateway
            var result = await paymentGateway.ProcessPaymentAsync(paymentRequest);

            if (result.IsSuccess && result.Value.Status == PaymentResultStatus.Succeeded)
            {
                var transactionId = Guid.Parse(result.Value.TransactionId.Replace("test_txn_", "").PadRight(32, '0').Substring(0, 32));

                logger.LogInformation(
                    "Payment successful for Order {OrderId}. TransactionId: {TransactionId}",
                    command.OrderId,
                    transactionId);

                // Return success event as cascading message
                return new PaymentSucceeded(
                    command.OrderId,
                    transactionId,
                    command.Amount);
            }
            else
            {
                var errorMessage = result.Error?.Description ?? "Payment processing failed";
                logger.LogWarning(
                    "Payment declined for Order {OrderId}. Reason: {Reason}",
                    command.OrderId,
                    errorMessage);

                return new PaymentFailed(
                    command.OrderId,
                    errorMessage,
                    result.Error?.Code ?? "GATEWAY_DECLINED");
            }
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
        IPaymentGateway paymentGateway,
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
            // Create refund request for the gateway
            var refundRequest = new RefundRequest(
                OriginalTransactionId: command.PaymentTransactionId.ToString(),
                Amount: command.Amount,
                Reason: command.Reason ?? "Order compensation");

            // Process refund through the gateway
            var result = await paymentGateway.ProcessRefundAsync(refundRequest);

            if (result.IsSuccess && result.Value.Success)
            {
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
            else
            {
                var errorMessage = result.Error?.Description ?? "Refund processing failed";
                logger.LogCritical(
                    "CRITICAL: Refund failed for Order {OrderId}. " +
                    "Customer may have been charged without service. Manual intervention required! Reason: {Reason}",
                    command.OrderId,
                    errorMessage);

                return new RefundFailed(command.OrderId, errorMessage);
            }
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
