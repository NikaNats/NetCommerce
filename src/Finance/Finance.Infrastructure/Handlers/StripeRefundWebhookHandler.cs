#nullable enable
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Finance.Domain.Audit;
using NetCommerce.Payments.Domain.Transactions;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Finance.Infrastructure.Handlers;

/// <summary>
///     Handles Stripe partial refund webhooks (charge.refunded).
///
///     <para>
///     <b>Partial Refund Scenarios:</b>
///     - Customer service issues partial refund via Stripe dashboard
///     - Automated refund for damaged/missing items
///     - Multiple sequential partial refunds
///     </para>
///
///     <para>
///     <b>Flow:</b>
///     1. Lookup order by charge/payment_intent ID
///     2. Record audit entry
///     3. Publish PartialRefundProcessed for saga to update order state
///     4. If total refunded = original amount, treat as full refund
///     </para>
/// </summary>
[WolverineHandler]
public static class StripeRefundWebhookHandler
{
    public static async Task<object?> Handle(
        ProcessStripeRefundWebhook command,
        IPaymentTransactionRepository transactionRepo,
        IFinancialAuditRepository auditRepo,
        ILogger logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Processing Stripe refund webhook: RefundId={RefundId}, ChargeId={ChargeId}, Amount={Amount} {Currency}",
            command.RefundId, command.ChargeId, command.AmountRefunded, command.Currency);

        // 1. Lookup order by external transaction ID (charge or payment_intent)
        var lookupId = command.PaymentIntentId ?? command.ChargeId;
        var transaction = await transactionRepo.GetByExternalIdAsync(lookupId, ct);

        if (transaction == null)
        {
            logger.LogWarning(
                "No internal transaction found for refund webhook. ChargeId={ChargeId}, PaymentIntentId={PaymentIntentId}. " +
                "This may be a refund for a payment not processed through our system.",
                command.ChargeId, command.PaymentIntentId);

            // Still audit the webhook - we received it, just couldn't match it
            await auditRepo.AppendAsync(FinancialAuditEntry.Create(
                FinancialAuditType.RefundInitiated,
                "UnmatchedRefund",
                command.RefundId,
                "StripeWebhook",
                ActorType.Webhook,
                $"Unmatched refund webhook received. ChargeId: {command.ChargeId}",
                externalTransactionId: command.ChargeId,
                amount: command.AmountRefunded,
                currency: command.Currency.ToUpperInvariant(),
                correlationId: Activity.Current?.Id), ct);

            return null;
        }

        var orderId = transaction.OrderId;
        var originalAmount = transaction.Amount.Amount;

        // 2. Determine if this is a partial or full refund
        var isFullRefund = Math.Abs(command.TotalRefundedSoFar - originalAmount) < 0.01m;
        var auditType = isFullRefund ? FinancialAuditType.RefundSucceeded : FinancialAuditType.PartialRefund;

        logger.LogInformation(
            "Refund matched to Order {OrderId}. Type={RefundType}, RefundedAmount={Amount}, TotalRefundedSoFar={Total}, OriginalAmount={Original}",
            orderId, isFullRefund ? "Full" : "Partial", command.AmountRefunded, command.TotalRefundedSoFar, originalAmount);

        // 3. Create audit entry
        var auditEntry = FinancialAuditEntry.Create(
            auditType,
            "Order",
            orderId.ToString(),
            "StripeWebhook",
            ActorType.Webhook,
            isFullRefund
                ? $"Full refund processed: {command.AmountRefunded:N2} {command.Currency.ToUpperInvariant()}"
                : $"Partial refund processed: {command.AmountRefunded:N2} {command.Currency.ToUpperInvariant()} (Total: {command.TotalRefundedSoFar:N2})",
            externalTransactionId: command.ChargeId,
            amount: command.AmountRefunded,
            currency: command.Currency.ToUpperInvariant(),
            metadata: System.Text.Json.JsonSerializer.Serialize(new
            {
                RefundId = command.RefundId,
                Reason = command.Reason,
                StripeEventId = command.StripeEventId
            }),
            correlationId: Activity.Current?.Id);

        await auditRepo.AppendAsync(auditEntry, ct);

        // 4. Publish event for saga/order to handle state updates
        return new PartialRefundProcessed(
            orderId,
            command.RefundId,
            command.AmountRefunded,
            command.TotalRefundedSoFar,
            originalAmount);
    }
}
