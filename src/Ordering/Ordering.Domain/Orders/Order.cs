using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
///     Order aggregate root with state machine workflow.
///     Implements Price Snapshotting pattern.
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
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
    /// <param name="amount">The charged amount from the PSP.</param>
    /// <param name="currency">The currency of the charge.</param>
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
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = GenerateOrderNumber("SHADOW"),
            CustomerId = Guid.Empty, // No customer - this is a reconciliation record
            Status = OrderStatus.Paid, // Already paid in PSP
            ShippingAddress = shippingAddress,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = $"shadow-{externalTxnId}",
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
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Cannot add items to non-submitted order");

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
                appliedWeightKg,
                sku,
                priceBreakdown);

            _items.Add(item);
        }

        RecalculateTotal();
    }

    public void SetBillingAddress(BillingAddress address)
    {
        BillingAddress = address;
    }

    /// <summary>
    ///     Called by background worker after grace period ends.
    ///     Transitions from Submitted to AwaitingValidation.
    /// </summary>
    public void ConfirmGracePeriod()
    {
        if (Status != OrderStatus.Submitted)
            return; // Idempotency check - already processed or cancelled

        Status = OrderStatus.AwaitingValidation;

        // Triggers Payment Processing via integration event
        RaiseDomainEvent(new OrderGracePeriodConfirmedDomainEvent(Id, OrderNumber, CustomerId, TotalAmount));
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
        if (Status == OrderStatus.Delivered || Status == OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel order. Current status: {Status}");

        var previousStatus = Status;
        var wasInGracePeriod = Status == OrderStatus.Submitted;

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason;

        // The event handler will check previousStatus to determine if refunds are needed
        // If previousStatus == Submitted: release stock only, no payment was taken
        // If previousStatus >= Paid: need to process refunds
        RaiseDomainEvent(new OrderCancelledDomainEvent(Id, reason, previousStatus));
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
        return $"{prefixPart}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
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
