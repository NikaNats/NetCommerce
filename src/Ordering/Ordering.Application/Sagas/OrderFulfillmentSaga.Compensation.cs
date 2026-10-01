using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;

namespace NetCommerce.Ordering.Application.Sagas;

/// <summary>
///     Cancellation, failure, and guarded-compensation handlers of the OrderFulfillmentSaga.
///     Moved verbatim from OrderFulfillmentSaga.cs (Failure Handlers + Compensation Result
///     regions, plus the CancelOrderFulfillmentCommand handler relocated from the Happy Path
///     region so all cancellation logic lives with its compensation siblings).
///     The duplicated XML summary on the InventoryConfirmationFailed handler was collapsed
///     to the fuller (second) block; no behavioral code changed.
/// </summary>
public sealed partial class OrderFulfillmentSaga
{
    /// <summary>
    ///     Handles customer or admin cancellation of a running fulfillment.
    ///     Stops the pipeline before any further charges: releases held inventory
    ///     and, if payment was already captured, issues a refund and parks the
    ///     saga in <see cref="OrderFulfillmentState.Compensating"/> until the
    ///     refund is verified (Guarded Compensation). Otherwise completes
    ///     immediately. Late timeouts arriving afterwards are ignored by the
    ///     strict state guards.
    /// </summary>
    public (
        ReleaseInventoryReservationCommand? ReleaseCommand,
        RefundPaymentCommand? RefundCommand,
        FailOrderCommand FailCommand,
        OrderStatusChanged Notification,
        CompensationStalledTimeoutMessage? StallTimeout
        ) Handle(
        CancelOrderFulfillmentCommand command,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogWarning(
            "Order {OrderId} ({OrderNumber}) cancelled: {Reason}. " +
            "Stopping fulfillment. Paid={IsPaid}, State={State}",
            Id,
            OrderNumber,
            command.Reason,
            IsPaid,
            State);

        // Release any inventory this saga may hold. The release handler resolves
        // reservations by OrderId, so this also covers reservations created but
        // not yet reported back when cancellation raced the reservation reply
        // (anything created afterwards expires via the reservation cleanup job).
        ReleaseInventoryReservationCommand? releaseCommand = null;
        if (State is OrderFulfillmentState.InGracePeriod
            or OrderFulfillmentState.LockingInventory
            or OrderFulfillmentState.ProcessingPayment
            or OrderFulfillmentState.ConfirmingInventory
            or OrderFulfillmentState.Compensating)
        {
            releaseCommand = new ReleaseInventoryReservationCommand(
                Id,
                $"Order cancelled: {command.Reason}");
        }

        RefundPaymentCommand? refundCommand = null;
        CompensationStalledTimeoutMessage? stallTimeout = null;

        if (IsPaid && !string.IsNullOrWhiteSpace(PaymentTransactionId))
        {
            refundCommand = new RefundPaymentCommand(
                Id,
                PaymentTransactionId!,
                TotalAmount,
                $"Order cancelled after payment: {command.Reason}");
            stallTimeout = new CompensationStalledTimeoutMessage { Id = Id };

            State = OrderFulfillmentState.Compensating;
            FailureReason = $"Order cancelled after payment: {command.Reason}";
            // Saga stays alive until RefundCompleted/RefundFailed (or escalation).
        }
        else
        {
            State = OrderFulfillmentState.Failed;
            FailureReason = $"Order cancelled: {command.Reason}";
            CompletedAt = DateTime.UtcNow;
            MarkCompleted();
        }

        var notification = new OrderStatusChanged(
            Id,
            "Error",
            "Your order has been cancelled.");

        return (releaseCommand, refundCommand, new FailOrderCommand(Id, command.Reason), notification, stallTimeout);
    }

    /// <summary>
    ///     Handles inventory reservation failure.
    ///     Compensation: None needed (nothing was reserved or charged yet).
    /// </summary>
    public (FailOrderCommand, OrderStatusChanged) Handle(
        InventoryReservationFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogWarning(
            "Inventory reservation failed for Order {OrderId}. Reason: {Reason}. " +
            "Unavailable products: {UnavailableProducts}",
            Id,
            @event.Reason,
            @event.UnavailableProductIds != null
                ? string.Join(", ", @event.UnavailableProductIds)
                : "N/A");

        // Update state
        State = OrderFulfillmentState.Failed;
        FailureReason = @event.Reason;
        CompletedAt = DateTime.UtcNow;

        // Mark saga as completed
        MarkCompleted();

        // Fail the order AND notify the browser
        var failCommand = new FailOrderCommand(Id, @event.Reason);
        var notification = new OrderStatusChanged(Id, "Error", "Sorry, some items are out of stock.");

        return (failCommand, notification);
    }

    /// <summary>
    ///     Handles payment failure.
    ///     Compensation: Release inventory reservation.
    /// </summary>
    public (
        ReleaseInventoryReservationCommand ReleaseCommand,
        FailOrderCommand FailCommand,
        OrderStatusChanged Notification
        ) Handle(
        PaymentFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogWarning(
            "Payment failed for Order {OrderId}. Reason: {Reason}, ErrorCode: {ErrorCode}. " +
            "Initiating compensating action: releasing inventory.",
            Id,
            @event.Reason,
            @event.ErrorCode);

        // Update state
        State = OrderFulfillmentState.Compensating;
        FailureReason = @event.Reason;

        // Compensating action: Release reserved inventory
        var releaseCommand = new ReleaseInventoryReservationCommand(
            Id,
            $"Payment failed: {@event.Reason}");

        // After compensation, update state and complete
        State = OrderFulfillmentState.Failed;
        CompletedAt = DateTime.UtcNow;
        MarkCompleted();

        var notification = new OrderStatusChanged(Id, "Error", "Payment failed. Please try again.");

        return (releaseCommand, new FailOrderCommand(Id, @event.Reason), notification);
    }

    /// <summary>
    ///     Handles inventory confirmation failure.
    ///     This is the CRITICAL failure scenario - payment was taken but inventory can't be confirmed.
    ///     Compensation: Refund payment AND release inventory.
    ///
    ///     IMPORTANT: The saga transitions to Compensating state and does NOT complete until the
    ///     refund is verified (GuardedCompensation pattern).
    ///
    ///     Pod-crash safety: the ReleaseInventoryReservation and RefundPayment commands are persisted
    ///     atomically by the Wolverine transactional outbox.  If the pod crashes immediately after
    ///     this handler runs, Wolverine re-delivers the commands on restart — no manual intervention
    ///     required for the normal crash-recovery path.
    ///
    ///     CompensationStalledTimeoutMessage fires after 4 hours to guard against the edge case where
    ///     a downstream service permanently rejects compensation (all retries exhausted → DLQ), leaving
    ///     the saga alive but stuck.
    /// </summary>
    public (
        RefundPaymentCommand RefundCommand,
        ReleaseInventoryReservationCommand ReleaseCommand,
        FailOrderCommand FailCommand,
        OrderStatusChanged Notification,
        CompensationStalledTimeoutMessage StallTimeout
        ) Handle(
        InventoryConfirmationFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogCritical(
            "CRITICAL: Inventory confirmation failed for Order {OrderId} AFTER payment. " +
            "PaymentTransactionId: {TransactionId}. Reason: {Reason}. " +
            "Transitioning to Compensating state. Saga will remain active until refund is confirmed.",
            Id,
            PaymentTransactionId,
            @event.Reason);

        // Update state to Compensating - DO NOT call MarkCompleted()
        State = OrderFulfillmentState.Compensating;
        FailureReason = @event.Reason;

        // Compensating actions persisted in the same DB transaction as this state transition
        var refundCommand = new RefundPaymentCommand(
            Id,
            PaymentTransactionId!,
            TotalAmount,
            $"Inventory confirmation failed: {@event.Reason}");

        var releaseCommand = new ReleaseInventoryReservationCommand(
            Id,
            $"Inventory confirmation failed: {@event.Reason}");

        // NOTE: Saga stays alive - awaiting RefundCompleted or RefundFailed events

        var notification = new OrderStatusChanged(
            Id,
            "Error",
            "Stock confirmation failed. Your payment will be refunded.");

        // Safety net: escalate to ManualInterventionRequired if compensation stalls for > 4 hours
        var stallTimeout = new CompensationStalledTimeoutMessage { Id = Id };

        return (refundCommand, releaseCommand, new FailOrderCommand(Id, @event.Reason), notification, stallTimeout);
    }

    /// <summary>
    ///     Handles successful refund confirmation.
    ///     This is the final step in the compensation workflow - the saga can now safely complete.
    ///     Implements the "Guarded Compensation" pattern by waiting for external system confirmation.
    /// </summary>
    public void Handle(RefundCompleted @event, ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Refund verified for Order {OrderId}. RefundTransactionId: {RefundTransactionId}, " +
            "Amount: {Amount}. Closing Saga state.",
            Id,
            @event.RefundTransactionId,
            @event.Amount);

        State = OrderFulfillmentState.Failed;
        CompletedAt = DateTime.UtcNow;

        // NOW and ONLY now is it safe to delete the saga from the DB
        MarkCompleted();
    }

    /// <summary>
    ///     Handles failed refund - the "nightmare scenario".
    ///     Money was charged but cannot be refunded automatically.
    ///     The saga remains in the database for manual intervention.
    /// </summary>
    public void Handle(RefundFailed @event, ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogError(
            "FATAL: Refund failed for Order {OrderId}. Money is stuck! " +
            "Reason: {Reason}. Saga will remain in database for manual intervention.",
            Id,
            @event.Reason);

        State = OrderFulfillmentState.ManualInterventionRequired;
        FailureReason = $"Refund failed: {@event.Reason}";

        // We do NOT call MarkCompleted().
        // This saga stays in the DB and shows up on the Admin Dashboard for human action.
        // StuckSagaAlertService sweeps this state every few minutes and pages on-call.
    }
}
