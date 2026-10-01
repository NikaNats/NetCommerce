using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;

namespace NetCommerce.Ordering.Application.Sagas;

/// <summary>
///     Success-path transitions of the OrderFulfillmentSaga.
///     Moved verbatim from OrderFulfillmentSaga.cs (Happy Path Handlers region) except
///     CancelOrderFulfillmentCommand (cancellation → Compensation) and GracePeriodTimeout
///     (timer → Timeouts), which live with their behavioral siblings.
/// </summary>
public sealed partial class OrderFulfillmentSaga
{
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
            OrderNumber,
            PaymentMethodId ?? string.Empty);

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
}
