using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;

namespace NetCommerce.Ordering.Application.Sagas;

/// <summary>
///     Timer handlers of the OrderFulfillmentSaga (grace period, reservation/payment/
///     confirmation timeouts, compensation-stall escalation).
///     Moved verbatim from OrderFulfillmentSaga.cs (Timeout Handlers region, plus the
///     GracePeriodTimeout handler relocated from the Happy Path region so all timers
///     live with their behavioral siblings). Every handler keeps its strict state guard:
///     late or duplicate timers in any other state are ignored.
/// </summary>
public sealed partial class OrderFulfillmentSaga
{
    /// <summary>
    ///     Handles grace period timeout (5 minutes elapsed).
    ///     If user hasn't cancelled, proceed to lock inventory and process payment.
    ///     This is the "point of no return" - payment will now be captured.
    ///
    ///     STRICT STATE GUARD: only fires from <see cref="OrderFulfillmentState.InGracePeriod"/>.
    ///     A late/duplicate timeout arriving in any other state (e.g. still reserving,
    ///     already paying, or compensating a cancellation) must be ignored —
    ///     otherwise the customer could be charged twice or charged after cancelling.
    /// </summary>
    public (
        LockInventoryForPaymentCommand? LockCommand,
        OrderStatusChanged? Notification
        ) Handle(
        GracePeriodTimeout timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        // Idempotency: only the grace-period state may advance to payment.
        // Cancellation completes the saga (or moves it to Compensating), so a
        // cancelled order can never fall through to LockInventoryForPayment.
        if (State != OrderFulfillmentState.InGracePeriod)
        {
            logger.LogInformation(
                "Ignoring grace period timeout for Order {OrderId}. " +
                "Current state: {State} (already processed or cancelled)",
                Id,
                State);
            // Return null tuple - Wolverine will not cascade null messages
            return (null, null);
        }

        logger.LogInformation(
            "Grace period expired for Order {OrderId}. User did not cancel. " +
            "Locking inventory and proceeding to payment.",
            Id);

        State = OrderFulfillmentState.LockingInventory;

        var lockCommand = new LockInventoryForPaymentCommand(Id, ReservedItems!);
        var notification = new OrderStatusChanged(
            Id,
            "ProcessingPayment",
            $"Grace period ended. Processing payment for order {OrderNumber}.");

        return (lockCommand, notification);
    }

    /// <summary>
    ///     Handles inventory reservation timeout.
    ///     If inventory wasn't reserved in time, cancel the order.
    /// </summary>
    public (FailOrderCommand, OrderStatusChanged)? Handle(
        InventoryReservationTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        // Idempotency: If we've already moved past this state, ignore the timeout
        if (State != OrderFulfillmentState.ReservingInventory)
        {
            logger.LogInformation(
                "Ignoring inventory reservation timeout for Order {OrderId}. " +
                "Current state: {State} (already processed)",
                Id,
                State);
            return null;
        }

        logger.LogWarning(
            "Inventory reservation timeout for Order {OrderId}. " +
            "Inventory service did not respond in time.",
            Id);

        State = OrderFulfillmentState.Failed;
        FailureReason = "Inventory reservation timed out";
        CompletedAt = DateTime.UtcNow;
        MarkCompleted();

        var notification = new OrderStatusChanged(
            Id,
            "Error",
            "Order processing timed out. Please try again.");

        return (new FailOrderCommand(Id, "Inventory reservation timed out"), notification);
    }

    /// <summary>
    ///     Handles payment timeout.
    ///     If payment wasn't processed in time, release inventory and cancel.
    /// </summary>
    public (
        ReleaseInventoryReservationCommand? ReleaseCommand,
        FailOrderCommand FailCommand,
        OrderStatusChanged Notification
        )? Handle(
        PaymentTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        // Idempotency: If we've already moved past payment, ignore the timeout
        if (State != OrderFulfillmentState.ProcessingPayment)
        {
            logger.LogInformation(
                "Ignoring payment timeout for Order {OrderId}. " +
                "Current state: {State} (already processed)",
                Id,
                State);
            return null;
        }

        logger.LogWarning(
            "Payment timeout for Order {OrderId}. Payment service did not respond in time.",
            Id);

        State = OrderFulfillmentState.Compensating;
        FailureReason = "Payment processing timed out";

        // Compensating action if inventory was reserved
        ReleaseInventoryReservationCommand? releaseCommand = null;
        if (IsInventoryReserved)
        {
            releaseCommand = new ReleaseInventoryReservationCommand(
                Id,
                "Payment processing timed out");
        }

        State = OrderFulfillmentState.Failed;
        CompletedAt = DateTime.UtcNow;
        MarkCompleted();

        var notification = new OrderStatusChanged(
            Id,
            "Error",
            "Payment processing timed out. Please try again.");

        return (releaseCommand, new FailOrderCommand(Id, "Payment processing timed out"), notification);
    }

    /// <summary>
    ///     Handles inventory confirmation timeout.
    ///     This is critical - payment was taken but confirmation is stuck.
    /// </summary>
    public (
        RefundPaymentCommand? RefundCommand,
        ReleaseInventoryReservationCommand ReleaseCommand,
        FailOrderCommand FailCommand,
        OrderStatusChanged Notification
        )? Handle(
        InventoryConfirmationTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        // Idempotency check
        if (State != OrderFulfillmentState.ConfirmingInventory)
        {
            logger.LogInformation(
                "Ignoring inventory confirmation timeout for Order {OrderId}. " +
                "Current state: {State} (already processed)",
                Id,
                State);
            return null;
        }

        logger.LogCritical(
            "CRITICAL: Inventory confirmation timeout for Order {OrderId}. " +
            "Payment was taken (TransactionId: {TransactionId}) but confirmation is stuck.",
            Id,
            PaymentTransactionId);

        State = OrderFulfillmentState.Compensating;
        FailureReason = "Inventory confirmation timed out";

        // Must refund since payment was taken
        RefundPaymentCommand? refundCommand = null;
        if (IsPaid && !string.IsNullOrWhiteSpace(PaymentTransactionId))
        {
            refundCommand = new RefundPaymentCommand(
                Id,
            PaymentTransactionId!,
                TotalAmount,
                "Inventory confirmation timed out");
        }

        var releaseCommand = new ReleaseInventoryReservationCommand(
            Id,
            "Inventory confirmation timed out");

        State = OrderFulfillmentState.Failed;
        CompletedAt = DateTime.UtcNow;
        MarkCompleted();

        var notification = new OrderStatusChanged(
            Id,
            "Error",
            "Order processing timed out. Your payment will be refunded.");

        return (refundCommand, releaseCommand, new FailOrderCommand(Id, "Inventory confirmation timed out"), notification);
    }

    /// <summary>
    ///     Fires 4 hours after entering the <see cref="OrderFulfillmentState.Compensating"/> state.
    ///
    ///     Normal path: the saga exits Compensating before this fires because
    ///     RefundCompleted/RefundFailed is received — this handler is a no-op.
    ///
    ///     Stalled path: downstream service permanently rejected compensation commands and they are
    ///     parked in the Wolverine dead-letter table. The saga stays alive indefinitely.
    ///     This handler escalates to ManualInterventionRequired, surfaces the order on the
    ///     Admin Recovery Dashboard, and stops silent data rot.
    ///
    ///     Recovery: use POST /api/admin/orders/{id}/force-cancel to close the saga,
    ///               and POST /api/admin/dlq/{id}/replay to re-queue the stalled DLQ message.
    /// </summary>
    public OrderStatusChanged? Handle(
        CompensationStalledTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        if (State != OrderFulfillmentState.Compensating)
        {
            logger.LogInformation(
                "Ignoring CompensationStalledTimeout for Order {OrderId}. " +
                "Current state: {State} — compensation already resolved.",
                Id, State);
            return null;
        }

        logger.LogCritical(
            "COMPENSATION STALLED: Order {OrderId} has been in Compensating state for > 4 hours. " +
            "FailureReason: {Reason}. PaymentTransactionId: {TransactionId}. " +
            "Escalating to ManualInterventionRequired. Check DLQ for stalled ReleaseInventory/Refund commands.",
            Id, FailureReason, PaymentTransactionId);

        State = OrderFulfillmentState.ManualInterventionRequired;

        // Do NOT call MarkCompleted() — saga must stay in DB for admin recovery

        return new OrderStatusChanged(
            Id,
            "ManualInterventionRequired",
            "Order compensation is stalled. The operations team has been alerted.");
    }
}
