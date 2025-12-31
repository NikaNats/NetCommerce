using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Inventory.Domain.Stock;

/// <summary>
/// Stock aggregate root for inventory management.
/// Supports soft reservations (15-minute holds).
/// </summary>
public sealed class Stock : AggregateRoot<Guid>
{
    private readonly List<StockReservation> _reservations = [];

    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    
    /// <summary>
    /// Total quantity in stock (includes reserved).
    /// </summary>
    public int Quantity { get; private set; }
    
    /// <summary>
    /// Low stock alert threshold.
    /// </summary>
    public int LowStockThreshold { get; private set; }
    
    public string? WarehouseLocation { get; private set; }
    public DateTime LastUpdatedAt { get; private set; }

    public IReadOnlyList<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>
    /// Available quantity (total minus reserved).
    /// </summary>
    public int AvailableQuantity => Quantity - _reservations
        .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt > DateTime.UtcNow)
        .Sum(r => r.Quantity);

    /// <summary>
    /// Reserved quantity from active reservations.
    /// </summary>
    public int ReservedQuantity => _reservations
        .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt > DateTime.UtcNow)
        .Sum(r => r.Quantity);

    public bool IsLowStock => AvailableQuantity <= LowStockThreshold;

    private Stock() { }

    public static Stock Create(
        Guid productId,
        string sku,
        int initialQuantity,
        int lowStockThreshold = 10,
        string? warehouseLocation = null)
    {
        if (initialQuantity < 0)
            throw new ArgumentException("Quantity cannot be negative", nameof(initialQuantity));

        return new Stock
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Sku = sku,
            Quantity = initialQuantity,
            LowStockThreshold = lowStockThreshold,
            WarehouseLocation = warehouseLocation,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a soft reservation that expires in 15 minutes.
    /// Used during checkout to hold stock temporarily.
    /// </summary>
    public StockReservation Reserve(Guid orderId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        // Clean up expired reservations first
        CleanupExpiredReservations();

        if (quantity > AvailableQuantity)
        {
            throw new InvalidOperationException(
                $"Insufficient stock. Available: {AvailableQuantity}, Requested: {quantity}");
        }

        var reservation = StockReservation.Create(Id, orderId, quantity);
        _reservations.Add(reservation);
        LastUpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new StockReservedDomainEvent(
            Id, ProductId, orderId, quantity, AvailableQuantity));

        if (IsLowStock)
        {
            RaiseDomainEvent(new LowStockAlertDomainEvent(Id, ProductId, Sku, AvailableQuantity));
        }

        return reservation;
    }

    /// <summary>
    /// Confirms a reservation and deducts from actual stock.
    /// Called after successful payment.
    /// </summary>
    public void ConfirmReservation(Guid reservationId)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);
        
        if (reservation is null)
            throw new InvalidOperationException($"Reservation {reservationId} not found");

        if (reservation.Status != ReservationStatus.Active)
            throw new InvalidOperationException($"Reservation is not active. Status: {reservation.Status}");

        reservation.Confirm();
        Quantity -= reservation.Quantity;
        LastUpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new StockDeductedDomainEvent(
            Id, ProductId, reservation.OrderId, reservation.Quantity, Quantity));
    }

    /// <summary>
    /// Releases a reservation back to available stock.
    /// Called when order is cancelled or reservation expires.
    /// </summary>
    public void ReleaseReservation(Guid reservationId)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);
        
        if (reservation is null)
            return;

        reservation.Release();
        LastUpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new StockReleasedDomainEvent(
            Id, ProductId, reservation.OrderId, reservation.Quantity, AvailableQuantity));
    }

    /// <summary>
    /// Adds stock (receiving inventory).
    /// </summary>
    public void AddStock(int quantity, string? reason = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        Quantity += quantity;
        LastUpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new StockAddedDomainEvent(Id, ProductId, quantity, Quantity, reason));
    }

    /// <summary>
    /// Removes stock directly (adjustments, damage, etc.).
    /// </summary>
    public void RemoveStock(int quantity, string reason)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        if (quantity > AvailableQuantity)
            throw new InvalidOperationException($"Cannot remove more than available. Available: {AvailableQuantity}");

        Quantity -= quantity;
        LastUpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new StockRemovedDomainEvent(Id, ProductId, quantity, Quantity, reason));
    }

    public void UpdateLowStockThreshold(int threshold)
    {
        LowStockThreshold = threshold;
    }

    private void CleanupExpiredReservations()
    {
        var now = DateTime.UtcNow;
        var expired = _reservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .ToList();

        foreach (var reservation in expired)
        {
            reservation.Expire();
            RaiseDomainEvent(new ReservationExpiredDomainEvent(
                Id, ProductId, reservation.OrderId, reservation.Quantity));
        }
    }
}
