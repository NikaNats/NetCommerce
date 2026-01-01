using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
/// Order aggregate root with state machine workflow.
/// Implements Price Snapshotting pattern.
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderItem> _items = [];

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
    /// Idempotency key for preventing duplicate order creation.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    private Order() { }

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
            Status = OrderStatus.Pending,
            ShippingAddress = shippingAddress,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey,
            Notes = notes,
            TotalAmount = Money.Zero()
        };

        order.RaiseDomainEvent(new OrderCreatedDomainEvent(order.Id, order.OrderNumber, customerId));
        
        return order;
    }

    /// <summary>
    /// Adds an item with SNAPSHOTTED price and title.
    /// This ensures historical order data is preserved.
    /// </summary>
    public void AddItem(
        Guid productId,
        string appliedTitle,  // Snapshot: product name at order time
        Money appliedPrice,   // Snapshot: price at order time
        int quantity,
        string? sku = null)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Cannot add items to non-pending order");

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
    /// Marks order as paid - transitions from Pending to Paid.
    /// </summary>
    public void MarkAsPaid(Guid paymentTransactionId)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException($"Cannot mark order as paid. Current status: {Status}");

        Status = OrderStatus.Paid;
        PaidAt = DateTime.UtcNow;
        PaymentTransactionId = paymentTransactionId;

        RaiseDomainEvent(new OrderPaidDomainEvent(Id, paymentTransactionId, OrderNumber, TotalAmount));
    }

    /// <summary>
    /// Transitions to Processing status.
    /// </summary>
    public void StartProcessing()
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidOperationException($"Cannot start processing. Current status: {Status}");

        Status = OrderStatus.Processing;
        RaiseDomainEvent(new OrderProcessingStartedDomainEvent(Id));
    }

    /// <summary>
    /// Marks order as shipped.
    /// </summary>
    public void MarkAsShipped(string? trackingNumber = null)
    {
        if (Status != OrderStatus.Processing)
            throw new InvalidOperationException($"Cannot mark as shipped. Current status: {Status}");

        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;

        RaiseDomainEvent(new OrderShippedDomainEvent(Id, trackingNumber));
    }

    /// <summary>
    /// Marks order as delivered.
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
    /// Cancels the order.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Delivered || Status == OrderStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel order. Current status: {Status}");

        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason;

        RaiseDomainEvent(new OrderCancelledDomainEvent(Id, reason, previousStatus));
    }

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
/// Order status workflow.
/// </summary>
public enum OrderStatus
{
    Pending = 0,      // Order created, awaiting payment
    Paid = 1,         // Payment received
    Processing = 2,   // Order being prepared
    Shipped = 3,      // Order shipped
    Delivered = 4,    // Order delivered
    Cancelled = 5     // Order cancelled
}
