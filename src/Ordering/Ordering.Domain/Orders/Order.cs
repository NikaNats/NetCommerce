using NetCommerce.SharedKernel.Domain;

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
    public Guid? PaymentTransactionId { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    ///     Idempotency key for preventing duplicate order creation.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

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
    ///     Adds an item with SNAPSHOTTED price and title.
    ///     This ensures historical order data is preserved.
    /// </summary>
    public void AddItem(
        Guid productId,
        string appliedTitle, // Snapshot: product name at order time
        Money appliedPrice, // Snapshot: price at order time
        int quantity,
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
                sku);

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
    public void MarkAsPaid(Guid paymentTransactionId)
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

    /// <summary>
    ///     Checks if the order is still within the grace period.
    /// </summary>
    public bool IsInGracePeriod => Status == OrderStatus.Submitted;

    private void RecalculateTotal()
    {
        var total = _items.Aggregate(
            Money.Zero(),
            (sum, item) => sum.Add(item.AppliedPrice.Multiply(item.Quantity)));

        TotalAmount = total;
    }

    private static string GenerateOrderNumber()
    {
        return $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
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