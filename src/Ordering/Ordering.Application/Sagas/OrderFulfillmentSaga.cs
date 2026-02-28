using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Domain.Shared.Events;
using Wolverine;

namespace NetCommerce.Ordering.Application.Sagas;

/// <summary>
///     Order Fulfillment Saga - Orchestrates the order fulfillment workflow.
///
///     This saga coordinates the following steps:
///     1. Reserve Inventory (soft reservation)
///     2. Process Payment
///     3. Confirm Inventory (hard deduction)
///     4. Finalize Order
///
///     Implements compensating transactions for failure scenarios:
///     - Payment failed → Release inventory reservation
///     - Inventory confirmation failed → Refund payment, release reservation
///     - Timeout → Cancel order, release resources
///
///     Architecture: Saga as Process Manager in the Ordering module (bounded context owner).
///
///     SERIALIZATION NOTE: This saga state is persisted to PostgreSQL by Wolverine.
///     The fully qualified type names are stored in the JSON.
///     Saga state uses NetCommerce.Domain.Shared.Money (canonical).
/// </summary>
public sealed class OrderFulfillmentSaga : Saga
{
    #region State Properties

    /// <summary>
    ///     Unique identifier for this saga instance.
    ///     Wolverine uses this to correlate messages to the correct saga.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     The customer who placed the order.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    ///     Human-readable order number for logging and display.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    ///     Total amount to be charged.
    /// </summary>
    public Money TotalAmount { get; set; } = Money.Zero();

    /// <summary>
    ///     Items in the order for inventory operations.
    /// </summary>
    public List<OrderItemReservation> Items { get; set; } = [];

    /// <summary>
    ///     Current state of the saga workflow.
    /// </summary>
    public OrderFulfillmentState State { get; set; } = OrderFulfillmentState.NotStarted;

    /// <summary>
    ///     Flag indicating inventory was successfully reserved.
    /// </summary>
    public bool IsInventoryReserved { get; set; }

    /// <summary>
    ///     Flag indicating inventory reservations are locked for payment.
    /// </summary>
    public bool IsInventoryLockedForPayment { get; set; }

    /// <summary>
    ///     Flag indicating payment was successfully processed.
    /// </summary>
    public bool IsPaid { get; set; }

    /// <summary>
    ///     Flag indicating inventory was confirmed (hard deduction).
    /// </summary>
    public bool IsInventoryConfirmed { get; set; }

    /// <summary>
    ///     Payment transaction ID for refunds.
    /// </summary>
    public string? PaymentTransactionId { get; set; }

    /// <summary>
    ///     Reserved items with their reservation IDs for release operations.
    /// </summary>
    public List<ReservedItem>? ReservedItems { get; set; }

    /// <summary>
    ///     Reason for saga failure (if failed).
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    ///     Timestamp when the saga started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    ///     Timestamp when the saga completed (success or failure).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    #endregion

    #region Saga Initiation

    /// <summary>
    ///     Starts the order fulfillment saga.
    ///     Convention: Static Start method creates the saga and returns cascading messages.
    ///
    ///     Strategy: Reserve inventory FIRST, then process payment.
    ///     This prevents charging customers for items that aren't available.
    /// </summary>
    public static (
        OrderFulfillmentSaga Saga,
        ReserveInventoryCommand ReserveCommand,
        InventoryReservationTimeoutMessage Timeout
        ) Start(
        StartOrderFulfillmentCommand command,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Starting OrderFulfillmentSaga for Order {OrderId} ({OrderNumber}). " +
            "Amount: {Amount}, Items: {ItemCount}",
            command.OrderId,
            command.OrderNumber,
            command.TotalAmount,
            command.Items.Count);

        var saga = new OrderFulfillmentSaga
        {
            Id = command.OrderId,
            CustomerId = command.CustomerId,
            OrderNumber = command.OrderNumber,
            TotalAmount = command.TotalAmount,
            Items = command.Items.ToList(),
            State = OrderFulfillmentState.ReservingInventory,
            StartedAt = DateTime.UtcNow
        };

        // Step 1: Reserve inventory first (before payment)
        var reserveCommand = new ReserveInventoryCommand(
            command.OrderId,
            command.Items);

        // Timeout in case inventory service doesn't respond
        var timeout = new InventoryReservationTimeoutMessage { Id = command.OrderId };

        return (saga, reserveCommand, timeout);
    }

    #endregion

    #region Happy Path Handlers

    /// <summary>
    ///     Handles successful inventory reservation.
    ///     CRITICAL: Stock is now secured. Start the 5-minute grace period.
    ///     This implements the "Strong Reservation Before Grace Period" pattern.
    /// </summary>
    public (
        OrderStatusChanged Notification,
        GracePeriodTimeout Timer
        ) Handle(
        InventoryReserved @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Inventory reserved for Order {OrderId}. Reserved {ItemCount} items. " +
            "Starting 5-minute grace period with stock secured.",
            Id,
            @event.ReservedItems.Count);

        // Update state
        IsInventoryReserved = true;
        ReservedItems = @event.ReservedItems.ToList();
        State = OrderFulfillmentState.InGracePeriod;

        // Notify user: "Stock is secured. We will process payment in 5 mins."
        var notification = new OrderStatusChanged(
            Id,
            "StockSecured",
            $"Your order {OrderNumber} is confirmed. Items are held exclusively for you. " +
            "You can cancel anytime in the next 5 minutes. Payment will be processed automatically.");

        // Schedule 5-minute delay before payment
        var timer = new GracePeriodTimeout { Id = Id };

        return (notification, timer);
    }

    /// <summary>
    ///     Handles grace period timeout (5 minutes elapsed).
    ///     If user hasn't cancelled, proceed to lock inventory and process payment.
    ///     This is the "point of no return" - payment will now be captured.
    /// </summary>
    public (
        LockInventoryForPaymentCommand? LockCommand,
        OrderStatusChanged? Notification
        ) Handle(
        GracePeriodTimeout timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        // If user cancelled during grace period, this won't execute (saga marked complete)
        if (State == OrderFulfillmentState.Completed || State == OrderFulfillmentState.Failed)
        {
            logger.LogInformation(
                "Grace period expired for Order {OrderId} but order was already {State}. Ignoring.",
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
    ///     Handles successful lock of inventory for payment; proceeds to payment processing.
    /// </summary>
    public (
        RequestPaymentCommand PaymentCommand,
        PaymentTimeoutMessage Timeout
        ) Handle(
        InventoryLocked @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Inventory locked for payment for Order {OrderId}. Proceeding to payment.",
            Id);

        IsInventoryLockedForPayment = true;
        State = OrderFulfillmentState.ProcessingPayment;

        var paymentCommand = new RequestPaymentCommand(
            Id,
            CustomerId,
            TotalAmount,
            OrderNumber);

        var timeout = new PaymentTimeoutMessage { Id = Id };

        return (paymentCommand, timeout);
    }

    /// <summary>
    ///     Handles payment initiation.
    ///
    ///     WEBHOOK-FIRST PATTERN:
    ///     Payment is initiated but status is Pending - awaiting webhook confirmation.
    ///     Saga remains in ProcessingPayment state, waiting for PaymentSucceeded event from webhook.
    ///
    ///     This acknowledges the payment was initiated with the provider (Stripe, PayPal, etc)
    ///     but actual charge confirmation will come via PaymentSucceeded event.
    /// </summary>
    public void Handle(
        PaymentInitiated @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Payment initiated for Order {OrderId}. PaymentId: {PaymentId}, ExternalTransactionId: {ExternalId}. " +
            "Awaiting webhook confirmation.",
            Id,
            @event.PaymentTransactionId,
            @event.ExternalTransactionId);

        // Store PaymentTransactionId for potential refund
        PaymentTransactionId = @event.ExternalTransactionId;

        // State remains ProcessingPayment - we're waiting for webhook
        // PaymentTimeoutMessage is already scheduled from previous handler
    }

    /// <summary>
    ///     Handles successful payment.
    ///     Proceeds to confirm inventory (hard deduction).
    ///
    ///     WEBHOOK-FIRST PATTERN:
    ///     This event is triggered by webhook confirmation, not by API response.
    /// </summary>
    public (
        ConfirmInventoryCommand ConfirmCommand,
        InventoryConfirmationTimeoutMessage Timeout
        ) Handle(
        PaymentSucceeded @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Payment succeeded for Order {OrderId}. TransactionId: {TransactionId}. " +
            "Confirming inventory. (Confirmed via webhook)",
            Id,
            @event.ExternalTransactionId);

        // Update state
        IsPaid = true;
        PaymentTransactionId = @event.ExternalTransactionId;
        State = OrderFulfillmentState.ConfirmingInventory;

        // Step 3: Confirm inventory (hard deduction)
        var confirmCommand = new ConfirmInventoryCommand(Id, @event.ExternalTransactionId);
        var timeout = new InventoryConfirmationTimeoutMessage { Id = Id };

        return (confirmCommand, timeout);
    }

    /// <summary>
    ///     Handles successful inventory confirmation.
    ///     Completes the saga successfully.
    /// </summary>
    public (FinalizeOrderCommand, OrderStatusChanged) Handle(
        InventoryConfirmed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Inventory confirmed for Order {OrderId}. Order fulfillment completed successfully!",
            Id);

        // Update state
        IsInventoryConfirmed = true;
        State = OrderFulfillmentState.Completed;
        CompletedAt = DateTime.UtcNow;

        // Mark saga as completed - will be purged from database
        MarkCompleted();

        // Finalize the order in the domain AND notify the browser
        var finalizeCommand = new FinalizeOrderCommand(Id, PaymentTransactionId!);
        var notification = new OrderStatusChanged(Id, "Success", "Your order has been confirmed!");

        return (finalizeCommand, notification);
    }

    #endregion

    #region Failure Handlers (Sad Path)

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
    ///     IMPORTANT: The saga transitions to Compensating state and does NOT complete until the refund is verified.
    ///     This implements the "Guarded Compensation" pattern for financial reliability.
    /// </summary>
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

    #endregion

    #region Compensation Result Handlers

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
        // This saga stays in the DB and shows up on an Admin Dashboard for human action.

        // TODO: Emit alert to operations dashboard/PagerDuty
    }

    #endregion

    #region Timeout Handlers

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

    #endregion

    #region NotFound Handlers (Late Messages for Deleted Sagas)

    /// <summary>
    ///     Handles late inventory reservation messages for completed/deleted sagas.
    ///     Prevents crashes when messages arrive after saga is purged.
    /// </summary>
    public static void NotFound(
        InventoryReserved @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryReserved for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryLocked @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryLocked for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryReservationFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryReservationFailed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        PaymentSucceeded @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late PaymentSucceeded for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        PaymentFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late PaymentFailed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryConfirmed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryConfirmed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryConfirmationFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryConfirmationFailed for Order {OrderId}. Saga already completed, ignoring.",
            @event.OrderId);
    }

    public static void NotFound(
        InventoryReservationTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryReservationTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    public static void NotFound(
        PaymentTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late PaymentTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    public static void NotFound(
        InventoryConfirmationTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late InventoryConfirmationTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    public static void NotFound(
        CompensationStalledTimeoutMessage timeout,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Received late CompensationStalledTimeout for Order {OrderId}. Saga already completed, ignoring.",
            timeout.Id);
    }

    #endregion
}

/// <summary>
///     States of the order fulfillment saga.
/// </summary>
public enum OrderFulfillmentState
{
    NotStarted = 0,
    ReservingInventory = 1,
    InGracePeriod = 2,          // Stock secured, 5-min cooling-off period
    LockingInventory = 3,
    ProcessingPayment = 4,
    ConfirmingInventory = 5,
    Compensating = 6,           // Refund requested, awaiting confirmation
    Completed = 7,              // Success
    Failed = 8,                 // Terminated after successful refund
    ManualInterventionRequired = 9 // The "Nightmare" state (Refund failed)
}
