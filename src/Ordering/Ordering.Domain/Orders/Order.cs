using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using CancelReason = global::NetCommerce.Ordering.Domain.Orders.CancellationReason;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Order aggregate root with state machine workflow.
///     Implements Price Snapshotting pattern.
/// </summary>
public sealed class Order : AggregateRoot<Guid>, IMultiTenant
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public string TenantId { get; set; } = string.Empty; // IMultiTenant implementation - requires public setter
    public OrderStatus Status { get; private set; }
    public Money TotalAmount { get; private set; } = default!;
    public ShippingAddress ShippingAddress { get; private set; } = default!;
    public BillingAddress? BillingAddress { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public string? PaymentTransactionId { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    ///     Idempotency key for preventing duplicate order creation.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>
    ///     Indicates this order was created as a shadow order during reconciliation.
    ///     Shadow orders are created to account for "ghost charges" - payments that exist
    ///     in the PSP but have no corresponding internal order record.
    /// </summary>
    public bool IsShadowOrder { get; private set; }

    /// <summary>
    ///     The external transaction ID from the payment provider that triggered
    ///     the shadow order creation during reconciliation.
    /// </summary>
    public string? SourceDiscrepancyTxnId { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    /// <summary>
    ///     Checks if the order is still within the grace period.
    /// </summary>
    public bool IsInGracePeriod => Status == OrderStatus.Submitted;

    public static Order Create(
        Guid customerId,
        ShippingAddress shippingAddress,
        string idempotencyKey,
        string? notes = null)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer is required.", nameof(customerId));
        ArgumentNullException.ThrowIfNull(shippingAddress);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = GenerateOrderNumber(),
            CustomerId = customerId,
            Status = OrderStatus.Submitted,
            ShippingAddress = shippingAddress,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey,
            Notes = notes,
            TotalAmount = Money.Zero()
        };

        // Triggers "Soft Reservation" in Inventory module via integration event
        order.RaiseDomainEvent(new OrderSubmittedDomainEvent(order.Id, order.OrderNumber, customerId));

        return order;
    }

    /// <summary>
    ///     Creates a "Shadow Order" during financial reconciliation.
    ///     This is used when a "ghost charge" is detected in the PSP (payment exists,
    ///     but no corresponding order record exists in the system).
    ///     Shadow orders are created directly in Paid status with the external transaction ID.
    /// </summary>
    /// <param name="externalTxnId">The PSP transaction ID from the discrepancy.</param>
    /// <param name="amount">The charged amount from the PSP (currency carried by Money).</param>
    /// <param name="shippingAddress">Minimal shipping address (may be partial for reconciliation).</param>
    /// <param name="resolvedBy">The admin who resolved the discrepancy.</param>
    /// <param name="notes">Audit notes explaining why this shadow order was created.</param>
    public static Order CreateShadowOrder(
        string externalTxnId,
        Money amount,
        ShippingAddress shippingAddress,
        string resolvedBy,
        string notes)
    {
        // Owned by Finance reconciliation (ghost-charge compensation).
        // Invoked via CreateShadowOrderCommand; do not call from normal checkout.
        if (string.IsNullOrWhiteSpace(externalTxnId))
            throw new ArgumentException("External transaction id is required.", nameof(externalTxnId));
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(shippingAddress);
        if (string.IsNullOrWhiteSpace(resolvedBy))
            throw new ArgumentException("Resolver is required for audit.", nameof(resolvedBy));
        if (string.IsNullOrWhiteSpace(notes))
            throw new ArgumentException("Audit notes are required.", nameof(notes));

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = GenerateOrderNumber("SHADOW"),
            CustomerId = Guid.Empty, // No customer - this is a reconciliation record
            Status = OrderStatus.Paid, // Already paid in PSP
            ShippingAddress = shippingAddress,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = IdempotencyKeys.ForShadowOrder(externalTxnId),
            Notes = $"[SHADOW ORDER] Created during reconciliation by {resolvedBy}. {notes}",
            TotalAmount = amount,
            IsShadowOrder = true,
            SourceDiscrepancyTxnId = externalTxnId,
            PaymentTransactionId = externalTxnId,
            PaidAt = DateTime.UtcNow
        };

        // Raise domain event for audit trail - no inventory or payment processing needed
        order.RaiseDomainEvent(new ShadowOrderCreatedDomainEvent(
            order.Id,
            order.OrderNumber,
            externalTxnId,
            amount,
            resolvedBy));

        return order;
    }

    /// <summary>
    ///     Adds an item with SNAPSHOTTED price, title, weight, and pricing breakdown.
    ///     This ensures historical order data is preserved, including physical weight for accurate shipping
    ///     and complete pricing breakdown for audit compliance.
    /// </summary>
    public void AddItem(
        Guid productId,
        string appliedTitle, // Snapshot: product name at order time
        Money appliedPrice, // Snapshot: final price at order time
        int quantity,
        decimal appliedWeightKg, // Snapshot: weight at order time
        PriceBreakdown priceBreakdown, // Snapshot: pricing breakdown at order time
        string? sku = null)
    {
        if (IsShadowOrder)
            throw new InvalidOperationException("Cannot add items to a reconciliation shadow order.");
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Cannot add items to non-submitted order");
        if (productId == Guid.Empty)
            throw new ArgumentException("Product is required.", nameof(productId));
        if (string.IsNullOrWhiteSpace(appliedTitle))
            throw new ArgumentException("Snapshot title is required.", nameof(appliedTitle));
        ArgumentNullException.ThrowIfNull(appliedPrice);
        ArgumentNullException.ThrowIfNull(priceBreakdown);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        var weight = WeightKg.Create(appliedWeightKg).Value;
        if (_items.Count > 0 && _items[0].AppliedPrice.Currency != appliedPrice.Currency)
            throw new InvalidOperationException(
                $"All items must share one currency: {_items[0].AppliedPrice.Currency} and {appliedPrice.Currency}.");

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            existingItem.UpdateQuantity(existingItem.Quantity + quantity);
        }
        else
        {
            var item = new OrderItem(
                Guid.NewGuid(),
                productId,
                appliedTitle,
                appliedPrice,
                quantity,
                weight,
                sku,
                priceBreakdown);

            _items.Add(item);
        }

        RecalculateTotal();
    }

    public void SetBillingAddress(BillingAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        BillingAddress = address;
    }

    /// <summary>
    ///     Called by background worker after grace period ends.
    ///     Transitions from Submitted to AwaitingValidation.
    ///     Idempotent: second calls are no-ops so retried workers/saga timeouts are safe.
    /// </summary>
    public void ConfirmGracePeriod()
    {
        TryConfirmGracePeriod();
    }

    /// <summary>
    ///     Tries the Submitted → AwaitingValidation transition.
    ///     Returns true when the transition ran, false when already processed.
    /// </summary>
    public bool TryConfirmGracePeriod()
    {
        if (IsShadowOrder || Status != OrderStatus.Submitted)
            return false; // Idempotency check - already processed, cancelled, or N/A

        Status = OrderStatus.AwaitingValidation;

        // Triggers Payment Processing via integration event
        RaiseDomainEvent(new OrderGracePeriodConfirmedDomainEvent(Id, OrderNumber, CustomerId, TotalAmount));
        return true;
    }

    /// <summary>
    ///     Called when stock is confirmed for the order.
    ///     Transitions from AwaitingValidation to StockConfirmed.
    /// </summary>
    public void ConfirmStock()
    {
        if (Status != OrderStatus.AwaitingValidation)
            throw new InvalidOperationException($"Cannot confirm stock. Current status: {Status}");

        Status = OrderStatus.StockConfirmed;

        RaiseDomainEvent(new OrderStockConfirmedDomainEvent(Id));
    }

    /// <summary>
    ///     Marks order as paid - transitions from StockConfirmed to Paid.
    /// </summary>
    public void MarkAsPaid(string paymentTransactionId)
    {
        if (Status != OrderStatus.StockConfirmed && Status != OrderStatus.AwaitingValidation)
            throw new InvalidOperationException($"Cannot mark order as paid. Current status: {Status}");

        Status = OrderStatus.Paid;
        PaidAt = DateTime.UtcNow;
        PaymentTransactionId = paymentTransactionId;

        RaiseDomainEvent(new OrderPaidDomainEvent(Id, paymentTransactionId, OrderNumber, TotalAmount));
    }

    /// <summary>
    ///     Transitions to Shipped status directly from Paid.
    ///     Note: Processing status removed in favor of simplified workflow.
    /// </summary>
    /// <summary>
    ///     Marks order as shipped.
    /// </summary>
    public void MarkAsShipped(string? trackingNumber = null)
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOperationException($"Cannot mark as shipped. Current status: {Status}");

        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;

        RaiseDomainEvent(new OrderShippedDomainEvent(Id, trackingNumber));
    }

    /// <summary>
    ///     Marks order as delivered.
    /// </summary>
    public void MarkAsDelivered()
    {
        if (Status != OrderStatus.Shipped)
            throw new InvalidOperationException($"Cannot mark as delivered. Current status: {Status}");

        Status = OrderStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;

        RaiseDomainEvent(new OrderDeliveredDomainEvent(Id));
    }

    /// <summary>
    ///     Cancels the order.
    ///     During grace period (Submitted status), cancellation is instant and free.
    ///     After grace period, may require refunds and compensating transactions.
    /// </summary>
    public void Cancel(string reason)
    {
        Cancel(CancelReason.Create(reason));
    }

    public void Cancel(CancelReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (!OrderCancellationPolicy.CanCancel(Status))
            throw new InvalidOperationException($"Cannot cancel order. Current status: {Status}");

        var previousStatus = Status;

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason.Value;

        // The event handler will check previousStatus to determine if refunds are needed
        // If previousStatus == Submitted: release stock only, no payment was taken
        // If previousStatus >= Paid: need to process refunds
        RaiseDomainEvent(new OrderCancelledDomainEvent(Id, reason.Value, previousStatus));
    }

    private void RecalculateTotal()
    {
        if (_items.Count == 0)
        {
            TotalAmount = Money.Zero();
            return;
        }

        // Use the currency of the first item (all items should have the same currency)
        var currency = _items[0].AppliedPrice.Currency;
        var total = _items.Aggregate(
            Money.Zero(currency),
            (sum, item) => sum.Add(item.AppliedPrice.Multiply(item.Quantity)));

        TotalAmount = total;
    }

    private static string GenerateOrderNumber(string? prefix = null)
    {
        var prefixPart = string.IsNullOrEmpty(prefix) ? "ORD" : prefix;
        return $"{prefixPart}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
    }
}

/// <summary>
///     Order status workflow with grace period support.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    ///     Order placed. Stock is soft reserved. Payment NOT taken.
    ///     User can cancel freely during grace period.
    /// </summary>
    Submitted = 0,

    /// <summary>
    ///     Grace period is over. Ready for payment capture.
    /// </summary>
    AwaitingValidation = 1,

    /// <summary>
    ///     Stock confirmed for the order.
    /// </summary>
    StockConfirmed = 2,

    /// <summary>
    ///     Payment received.
    /// </summary>
    Paid = 3,

    /// <summary>
    ///     Order shipped.
    /// </summary>
    Shipped = 4,

    /// <summary>
    ///     Order delivered.
    /// </summary>
    Delivered = 5,

    /// <summary>
    ///     Order cancelled.
    /// </summary>
    Cancelled = 6
}
