using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
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
    ///     Proceeds to payment processing.
    /// </summary>
    public (
        LockInventoryForPaymentCommand LockCommand,
        PaymentTimeoutMessage Timeout
        ) Handle(
        InventoryReserved @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Inventory reserved for Order {OrderId}. Reserved {ItemCount} items. Locking for payment.",
            Id,
            @event.ReservedItems.Count);

        // Update state
        IsInventoryReserved = true;
        ReservedItems = @event.ReservedItems.ToList();
        State = OrderFulfillmentState.LockingInventory;

        var lockCommand = new LockInventoryForPaymentCommand(Id, @event.ReservedItems);
        var timeout = new PaymentTimeoutMessage { Id = Id };

        return (lockCommand, timeout);
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
    ///     Handles successful payment.
    ///     Proceeds to confirm inventory (hard deduction).
    /// </summary>
    public (
        ConfirmInventoryCommand ConfirmCommand,
        InventoryConfirmationTimeoutMessage Timeout
        ) Handle(
        PaymentSucceeded @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Payment succeeded for Order {OrderId}. TransactionId: {TransactionId}. Confirming inventory.",
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
    /// </summary>
    public (
        RefundPaymentCommand RefundCommand,
        ReleaseInventoryReservationCommand ReleaseCommand,
        FailOrderCommand FailCommand,
        OrderStatusChanged Notification
        ) Handle(
        InventoryConfirmationFailed @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogCritical(
            "CRITICAL: Inventory confirmation failed for Order {OrderId} AFTER payment. " +
            "PaymentTransactionId: {TransactionId}. Reason: {Reason}. " +
            "Initiating compensating actions: refund + release inventory.",
            Id,
            PaymentTransactionId,
            @event.Reason);

        // Update state
        State = OrderFulfillmentState.Compensating;
        FailureReason = @event.Reason;

        // Compensating actions
        var refundCommand = new RefundPaymentCommand(
            Id,
            PaymentTransactionId!,
            TotalAmount,
            $"Inventory confirmation failed: {@event.Reason}");

        var releaseCommand = new ReleaseInventoryReservationCommand(
            Id,
            $"Inventory confirmation failed: {@event.Reason}");

        // Complete saga after issuing compensations
        State = OrderFulfillmentState.Failed;
        CompletedAt = DateTime.UtcNow;
        MarkCompleted();

        var notification = new OrderStatusChanged(
            Id,
            "Error",
            "Stock confirmation failed. Your payment will be refunded.");

        return (refundCommand, releaseCommand, new FailOrderCommand(Id, @event.Reason), notification);
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

    #endregion
}

/// <summary>
///     States of the order fulfillment saga.
/// </summary>
public enum OrderFulfillmentState
{
    NotStarted = 0,
    ReservingInventory = 1,
    LockingInventory = 2,
    ProcessingPayment = 3,
    ConfirmingInventory = 4,
    Compensating = 5,
    Completed = 6,
    Failed = 7
}
