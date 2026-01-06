using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Domain.Shared.Events;
using Wolverine.Attributes;
using Wolverine;

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
    ///
    ///     WEBHOOK-FIRST PATTERN (2025 Gold Standard):
    ///     1. Create PaymentTransaction with Status=Pending
    ///     2. Call gateway.ProcessPaymentAsync (returns Pending with ExternalTransactionId)
    ///     3. Store ExternalTransactionId
    ///     4. Return PaymentInitiated event (NOT PaymentSucceeded)
    ///     5. Webhook will later trigger PaymentCompletedDomainEvent → saga continues
    ///
    ///     Prevents "Ghost Charge" vulnerability where customer is charged but order is lost.
    /// </summary>
    [Transactional]
    public static async Task<object> Handle(
        RequestPaymentCommand command,
        IPaymentGateway paymentGateway,
        IPaymentTransactionRepository repository,
        Envelope envelope,
        ILogger<RequestPaymentCommand> logger)
    {
        logger.LogInformation(
            "Processing payment request for Order {OrderId} ({OrderNumber}). Amount: {Amount}. MessageId: {MessageId}",
            command.OrderId,
            command.OrderNumber,
            command.Amount,
            envelope.Id);

        try
        {
            // 1. Create PaymentTransaction (internal ledger)
            var paymentTransaction = PaymentTransaction.Create(
                orderId: command.OrderId,
                amount: command.Amount,
                provider: (Domain.Transactions.PaymentProvider)paymentGateway.Provider,
                idempotencyKey: $"payment_{envelope.Id}");

            await repository.AddAsync(paymentTransaction);

            // 2. Initiate payment with provider (returns Pending)
            var paymentRequest = new PaymentRequest(
                OrderId: command.OrderId,
                Amount: command.Amount,
                PaymentMethodToken: "tok_visa",
                IdempotencyKey: paymentTransaction.IdempotencyKey!,
                Description: $"Payment for order {command.OrderNumber}");

            var result = await paymentGateway.ProcessPaymentAsync(paymentRequest);

            if (result.IsFailure)
            {
                // Gateway error (network, configuration, etc)
                var errorMessage = result.Error?.Description ?? "Payment gateway error";

                paymentTransaction.MarkAsFailed(errorMessage);
                repository.Update(paymentTransaction);

                logger.LogError(
                    "Payment gateway error for Order {OrderId}. Error: {Error}",
                    command.OrderId,
                    errorMessage);

                return new PaymentFailed(
                    command.OrderId,
                    errorMessage,
                    result.Error?.Code ?? "GATEWAY_ERROR");
            }

            var paymentResult = result.Value;

            // 3. Handle immediate failures (card declined, etc)
            if (paymentResult.Status == PaymentResultStatus.Failed)
            {
                paymentTransaction.MarkAsFailed(paymentResult.ErrorMessage ?? "Payment declined");
                repository.Update(paymentTransaction);

                logger.LogWarning(
                    "Payment declined for Order {OrderId}. Reason: {Reason}",
                    command.OrderId,
                    paymentResult.ErrorMessage);

                return new PaymentFailed(
                    command.OrderId,
                    paymentResult.ErrorMessage ?? "Payment declined",
                    "CARD_DECLINED");
            }

            // 4. Store ExternalTransactionId
            paymentTransaction.SetExternalTransactionId(paymentResult.TransactionId);
            repository.Update(paymentTransaction);

            logger.LogInformation(
                "Payment initiated for Order {OrderId}. " +
                "PaymentId: {PaymentId}, ExternalTransactionId: {ExternalId}, Status: {Status}. " +
                "Awaiting webhook confirmation.",
                command.OrderId,
                paymentTransaction.Id,
                paymentResult.TransactionId,
                paymentResult.Status);

            // 5. Return PaymentInitiated event (saga waits for webhook)
            // NOTE: Saga will receive PaymentCompletedDomainEvent from webhook later
            return new PaymentInitiated(
                command.OrderId,
                paymentTransaction.Id,
                paymentResult.TransactionId,
                command.Amount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Payment failed for Order {OrderId}. Error: {Error}",
                command.OrderId,
                ex.Message);
            throw;
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
                OriginalTransactionId: command.PaymentTransactionId,
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
