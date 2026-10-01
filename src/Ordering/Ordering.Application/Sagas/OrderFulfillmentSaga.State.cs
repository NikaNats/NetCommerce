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
///
///     DECOMPOSITION NOTE: This type is split into concern-based partials. Wolverine's
///     static codegen keys saga identity off the single OrderFulfillmentSaga type, so the
///     split is file-level only — State (here), HappyPath (success transitions + grace
///     timer entry), Compensation (cancel/failures/refund outcomes), Timeouts (all timers),
///     NotFound (late messages for purged sagas). No handler was added, removed, or reordered.
/// </summary>
public sealed partial class OrderFulfillmentSaga : Saga
{
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
    ///     Client-supplied payment method token (e.g. Stripe pm_ id).
    ///     Empty when the order was created without one (legacy / grace-period flow).
    /// </summary>
    public string PaymentMethodId { get; set; } = string.Empty;

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
            PaymentMethodId = command.PaymentMethodId ?? string.Empty,
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
