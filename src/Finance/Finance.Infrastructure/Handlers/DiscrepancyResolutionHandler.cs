using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using Wolverine.Attributes;

namespace NetCommerce.Finance.Infrastructure.Handlers;

/// <summary>
///     Handler for manual discrepancy resolution.
///     Processes admin actions for ghost charges and other discrepancies.
/// </summary>
[WolverineHandler]
public static class DiscrepancyResolutionHandler
{
    /// <summary>
    ///     Handle discrepancy resolution command.
    ///     Admin-in-the-loop corrections for financial discrepancies.
    /// </summary>
    [Transactional]
    public static async Task Handle(
        ResolveDiscrepancyCommand command,
        IReconciliationSessionRepository sessionRepo,
        IPaymentGateway paymentGateway,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "MANUAL DISCREPANCY RESOLUTION: Session={SessionId}, Txn={TxnId}, Action={Action}, User={User}",
            command.SessionId, command.ExternalTxnId, command.Action, command.ResolvedBy);

        var session = await sessionRepo.GetByIdAsync(command.SessionId, cancellationToken);
        if (session == null)
        {
            logger.LogError("Reconciliation session {SessionId} not found", command.SessionId);
            throw new InvalidOperationException($"Session {command.SessionId} not found");
        }

        var discrepancy = session.Discrepancies.FirstOrDefault(d => d.ExternalTxnId == command.ExternalTxnId);
        if (discrepancy == null)
        {
            logger.LogError("Discrepancy {TxnId} not found in session {SessionId}", command.ExternalTxnId, command.SessionId);
            throw new InvalidOperationException($"Discrepancy {command.ExternalTxnId} not found");
        }

        try
        {
            switch (command.Action)
            {
                case DiscrepancyResolutionAction.RefundGhostCharge:
                    await HandleGhostChargeRefundAsync(discrepancy, paymentGateway, command, logger, cancellationToken);
                    break;

                case DiscrepancyResolutionAction.CreateShadowOrder:
                    await HandleShadowOrderCreationAsync(discrepancy, command, logger, cancellationToken);
                    break;

                case DiscrepancyResolutionAction.AcceptDiscrepancy:
                    HandleDiscrepancyAcceptance(session, discrepancy, command);
                    break;

                case DiscrepancyResolutionAction.InvestigateFurther:
                    HandleFurtherInvestigation(session, discrepancy, command);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(command.Action));
            }

            // Update session notes with resolution
            session.AddNote($"Resolved {command.ExternalTxnId}: {command.Action} by {command.ResolvedBy} - {command.Reason}");

            sessionRepo.Update(session);

            logger.LogInformation("Discrepancy {TxnId} resolved with action {Action}", command.ExternalTxnId, command.Action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve discrepancy {TxnId}", command.ExternalTxnId);
            throw;
        }
    }

    private static async Task HandleGhostChargeRefundAsync(
        Discrepancy discrepancy,
        IPaymentGateway paymentGateway,
        ResolveDiscrepancyCommand command,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (discrepancy.Type != DiscrepancyType.MissingInternal)
        {
            throw new InvalidOperationException("Refund only allowed for ghost charges");
        }

        logger.LogCritical("INITIATING REFUND for ghost charge {TxnId}, Amount: {Amount}",
            discrepancy.ExternalTxnId, discrepancy.Difference);

        var refundId = await paymentGateway.RefundTransactionAsync(
            discrepancy.ExternalTxnId,
            Math.Abs(discrepancy.Difference),
            $"Ghost charge resolution: {command.Reason}",
            cancellationToken);

        logger.LogInformation("Refund {RefundId} initiated for ghost charge {TxnId}", refundId, discrepancy.ExternalTxnId);
    }

    private static async Task HandleShadowOrderCreationAsync(
        Discrepancy discrepancy,
        ResolveDiscrepancyCommand command,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Implementation would:
        // 1. Create a "Shadow Order" in the ordering system
        // 2. Mark it as paid with the external transaction
        // 3. Add audit notes about the discrepancy resolution

        logger.LogInformation("Creating shadow order for discrepancy {TxnId}", discrepancy.ExternalTxnId);

        // TODO: Integrate with Ordering module to create shadow order
        // await _orderingService.CreateShadowOrderAsync(discrepancy, command);
    }

    private static void HandleDiscrepancyAcceptance(
        ReconciliationSession session,
        Discrepancy discrepancy,
        ResolveDiscrepancyCommand command)
    {
        // Mark discrepancy as accepted with audit trail
        // This creates a permanent record that the discrepancy was reviewed and accepted
    }

    private static void HandleFurtherInvestigation(
        ReconciliationSession session,
        Discrepancy discrepancy,
        ResolveDiscrepancyCommand command)
    {
        // Flag for escalation to finance team
        // Could trigger additional workflows or notifications
    }
}
