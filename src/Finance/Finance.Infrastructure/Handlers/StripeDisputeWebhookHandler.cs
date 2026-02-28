#nullable enable
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Audit;
using NetCommerce.Payments.Domain.Transactions;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Finance.Infrastructure.Handlers;

/// <summary>
///     Handles Stripe dispute webhooks (charge.dispute.created, charge.dispute.updated).
///
///     <para>
///     <b>Dispute (Chargeback) Criticality:</b>
///     - Customer's bank initiated reversal — money already debited from our account
///     - Evidence must be submitted before deadline or dispute is auto-lost
///     - Too many disputes can result in Stripe account termination
///     </para>
///
///     <para>
///     <b>Dispute Lifecycle:</b>
///     - needs_response: Evidence required (deadline in EvidenceDueBy)
///     - under_review: Evidence submitted, awaiting bank decision
///     - won: We kept the money, dispute closed in our favor
///     - lost: Customer refunded by bank, we lost the money + fee
///     - charge_refunded: We refunded before dispute resolved
///     - warning_closed: Inquiry closed without chargeback
///     </para>
/// </summary>
[WolverineHandler]
public static class StripeDisputeWebhookHandler
{
    /// <summary>
    ///     Handles charge.dispute.created — CRITICAL: requires immediate attention.
    /// </summary>
    public static async Task<object[]> Handle(
        ProcessStripeDisputeCreated command,
        IPaymentTransactionRepository transactionRepo,
        IFinancialAuditRepository auditRepo,
        ILogger logger,
        CancellationToken ct)
    {
        logger.LogCritical(
            "🚨 DISPUTE CREATED: DisputeId={DisputeId}, ChargeId={ChargeId}, Amount={Amount} {Currency}, " +
            "Reason={Reason}, Status={Status}, EvidenceDueBy={DueBy}",
            command.DisputeId, command.ChargeId, command.Amount, command.Currency,
            command.Reason, command.Status, command.EvidenceDueBy);

        // 1. Lookup order
        var transaction = await transactionRepo.GetByExternalIdAsync(command.ChargeId, ct);
        Guid? orderId = transaction?.OrderId;

        // 2. Audit entry — disputes are ALWAYS recorded even if order not found
        var auditEntry = FinancialAuditEntry.Create(
            FinancialAuditType.DisputeCreated,
            "Dispute",
            command.DisputeId,
            "StripeWebhook",
            ActorType.Webhook,
            $"Dispute created: {command.Reason}. Evidence due by {command.EvidenceDueBy:yyyy-MM-dd HH:mm} UTC",
            externalTransactionId: command.ChargeId,
            amount: command.Amount,
            currency: command.Currency.ToUpperInvariant(),
            metadata: System.Text.Json.JsonSerializer.Serialize(new
            {
                DisputeId = command.DisputeId,
                Reason = command.Reason,
                Status = command.Status,
                EvidenceDueBy = command.EvidenceDueBy,
                StripeEventId = command.StripeEventId,
                OrderId = orderId
            }),
            correlationId: Activity.Current?.Id);

        await auditRepo.AppendAsync(auditEntry, ct);

        // 3. Generate outputs: Critical alert + Order event
        var outputs = new List<object>
        {
            // CRITICAL ALERT — disputes escalate to PagerDuty/finance team
            new CriticalFinancialAlert(
                command.ChargeId,
                command.Amount,
                $"CHARGEBACK: {command.Reason}. Evidence due: {command.EvidenceDueBy:yyyy-MM-dd HH:mm} UTC. DisputeId: {command.DisputeId}")
        };

        if (orderId.HasValue)
        {
            outputs.Add(new DisputeCreatedForOrder(
                orderId.Value,
                command.DisputeId,
                command.Amount,
                command.Reason,
                command.EvidenceDueBy));
        }
        else
        {
            logger.LogWarning(
                "Dispute {DisputeId} received but no matching order found for ChargeId {ChargeId}",
                command.DisputeId, command.ChargeId);
        }

        return outputs.ToArray();
    }

    /// <summary>
    ///     Handles charge.dispute.updated — track lifecycle status changes.
    /// </summary>
    public static async Task<object?> Handle(
        ProcessStripeDisputeUpdated command,
        IPaymentTransactionRepository transactionRepo,
        IFinancialAuditRepository auditRepo,
        ILogger logger,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Dispute updated: DisputeId={DisputeId}, ChargeId={ChargeId}, Status={Status}",
            command.DisputeId, command.ChargeId, command.Status);

        // Map Stripe status to our outcome enum
        var outcome = command.Status.ToLowerInvariant() switch
        {
            "won" => DisputeOutcome.Won,
            "lost" => DisputeOutcome.Lost,
            "charge_refunded" => DisputeOutcome.ChargeRefunded,
            "warning_closed" => DisputeOutcome.WarningClosed,
            _ => (DisputeOutcome?)null
        };

        var auditType = command.Status.ToLowerInvariant() switch
        {
            "won" => FinancialAuditType.DisputeWon,
            "lost" => FinancialAuditType.DisputeLost,
            _ => FinancialAuditType.DisputeUpdated
        };

        // Log with appropriate severity
        if (outcome == DisputeOutcome.Lost)
        {
            logger.LogError(
                "❌ DISPUTE LOST: DisputeId={DisputeId}, ChargeId={ChargeId}. Money and fee debited.",
                command.DisputeId, command.ChargeId);
        }
        else if (outcome == DisputeOutcome.Won)
        {
            logger.LogInformation(
                "✅ DISPUTE WON: DisputeId={DisputeId}, ChargeId={ChargeId}. Funds retained.",
                command.DisputeId, command.ChargeId);
        }

        // Audit entry
        await auditRepo.AppendAsync(FinancialAuditEntry.Create(
            auditType,
            "Dispute",
            command.DisputeId,
            "StripeWebhook",
            ActorType.Webhook,
            $"Dispute status changed to: {command.Status}",
            externalTransactionId: command.ChargeId,
            metadata: System.Text.Json.JsonSerializer.Serialize(new
            {
                DisputeId = command.DisputeId,
                Status = command.Status,
                StripeEventId = command.StripeEventId,
                Outcome = outcome?.ToString()
            }),
            correlationId: Activity.Current?.Id), ct);

        // If this is a terminal status, publish resolution event
        if (outcome.HasValue)
        {
            var transaction = await transactionRepo.GetByExternalIdAsync(command.ChargeId, ct);
            if (transaction != null)
            {
                return new DisputeResolved(
                    transaction.OrderId,
                    command.DisputeId,
                    outcome.Value);
            }
        }

        return null;
    }
}
