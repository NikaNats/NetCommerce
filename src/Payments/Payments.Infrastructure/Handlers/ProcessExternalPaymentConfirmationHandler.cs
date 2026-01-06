#region

using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Domain.Shared.Events;
using Wolverine.Attributes;

#endregion

namespace NetCommerce.Payments.Infrastructure.Handlers;

/// <summary>
///     Handler for external payment confirmations from payment provider webhooks.
///     WEBHOOK-FIRST PATTERN (2025 Gold Standard)
///     - ProcessPaymentAsync returns "Pending" (not "Succeeded")
///     - Webhook calls this handler with actual payment status
///     - Handler is idempotent (safe to process duplicate webhooks)
///     - Uses [Transactional] for exactly-once processing
///     Prevents "Ghost Charge" vulnerability where customer is charged but order is lost.
/// </summary>
[WolverineHandler]
public static class ProcessExternalPaymentConfirmationHandler
{
    /// <summary>
    ///     Process payment confirmation from external provider webhook.
    ///     Idempotency Strategy:
    ///     - If payment already Completed, ignore (duplicate webhook)
    ///     - If payment not found, log warning (webhook for old deployment)
    ///     - Uses ExternalTransactionId as natural idempotency key
    ///     Flow:
    ///     1. Find PaymentTransaction by ExternalTransactionId
    ///     2. Check if already completed (idempotency)
    ///     3. Update status based on webhook event
    ///     4. PaymentCompletedDomainEvent triggers saga continuation
    /// </summary>
    [Transactional] // Wolverine ensures exactly-once processing
    public static async Task Handle(
        ProcessExternalPaymentConfirmation command,
        IPaymentTransactionRepository repository,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing external payment confirmation for ExternalTransactionId: {ExternalId}, Status: {Status}, WebhookEventId: {WebhookEventId}",
            command.ExternalTransactionId,
            command.Status,
            command.WebhookEventId);

        // Find payment by ExternalTransactionId
        PaymentTransaction? payment =
            await repository.GetByExternalIdAsync(command.ExternalTransactionId, cancellationToken);

        if (payment == null)
        {
            logger.LogWarning(
                "Payment not found for ExternalTransactionId: {ExternalId}, WebhookEventId: {WebhookEventId}. " +
                "This could be a webhook for a payment initiated before deployment, or a test webhook from Stripe dashboard.",
                command.ExternalTransactionId,
                command.WebhookEventId);
            return;
        }

        // IDEMPOTENCY: If already completed, ignore (duplicate webhook)
        if (payment.Status == PaymentStatus.Completed)
        {
            logger.LogInformation(
                "Payment {PaymentId} for Order {OrderId} already completed. " +
                "Ignoring duplicate webhook. WebhookEventId: {WebhookEventId}",
                payment.Id,
                payment.OrderId,
                command.WebhookEventId);
            return;
        }

        // IDEMPOTENCY: If already failed, ignore
        if (payment.Status == PaymentStatus.Failed)
        {
            logger.LogInformation(
                "Payment {PaymentId} for Order {OrderId} already failed. " +
                "Ignoring duplicate webhook. WebhookEventId: {WebhookEventId}",
                payment.Id,
                payment.OrderId,
                command.WebhookEventId);
            return;
        }

        // Update status based on webhook event
        if (command.Status == "Succeeded")
        {
            payment.MarkAsCompleted(command.ExternalTransactionId);

            logger.LogInformation(
                "Payment {PaymentId} for Order {OrderId} marked as completed via webhook confirmation. " +
                "ExternalTransactionId: {ExternalId}, WebhookEventId: {WebhookEventId}",
                payment.Id,
                payment.OrderId,
                command.ExternalTransactionId,
                command.WebhookEventId);
        }
        else if (command.Status == "Failed" || command.Status == "Canceled")
        {
            payment.MarkAsFailed($"Webhook status: {command.Status}, WebhookEventId: {command.WebhookEventId}");

            logger.LogWarning(
                "Payment {PaymentId} for Order {OrderId} marked as failed via webhook. " +
                "Status: {Status}, WebhookEventId: {WebhookEventId}",
                payment.Id,
                payment.OrderId,
                command.Status,
                command.WebhookEventId);
        }
        else
        {
            logger.LogWarning(
                "Unknown webhook status for Payment {PaymentId}: {Status}. Ignoring. WebhookEventId: {WebhookEventId}",
                payment.Id,
                command.Status,
                command.WebhookEventId);
            return;
        }

        // Save changes
        repository.Update(payment);

        // Domain event (PaymentCompletedDomainEvent or PaymentFailedDomainEvent)
        // will be published automatically by Wolverine and trigger saga continuation

        logger.LogInformation(
            "Payment {PaymentId} status updated. Domain events will trigger saga continuation.",
            payment.Id);
    }
}
