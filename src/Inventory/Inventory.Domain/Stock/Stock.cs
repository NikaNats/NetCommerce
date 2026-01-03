using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Inventory.Domain.Stock;

/// <summary>
///     Stock aggregate root for inventory management.
///     Supports soft reservations (15-minute holds).
///     Uses TimeProvider for deterministic time operations.
/// </summary>
public sealed class Stock : AggregateRoot<Guid>
{
    private readonly List<StockReservation> _reservations = [];

    private Stock()
    {
    }

    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;

    /// <summary>
    ///     Total quantity in stock (includes reserved).
    /// </summary>
    public int Quantity { get; private set; }

    /// <summary>
    ///     Low stock alert threshold.
    /// </summary>
    public int LowStockThreshold { get; private set; }

    public string? WarehouseLocation { get; private set; }
    public DateTime LastUpdatedAt { get; private set; }

    public IReadOnlyList<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>
    ///     Gets the available quantity using the specified time provider.
    ///     Calculates total minus active (non-expired) reservations.
    /// </summary>
    public int GetAvailableQuantity(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return Quantity - _reservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt > now)
            .Sum(r => r.Quantity);
    }

    /// <summary>
    ///     Gets the reserved quantity using the specified time provider.
    ///     Sum of active (non-expired) reservations.
    /// </summary>
    public int GetReservedQuantity(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return _reservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt > now)
            .Sum(r => r.Quantity);
    }

    /// <summary>
    ///     Available quantity (total minus reserved).
    ///     Uses system time - prefer GetAvailableQuantity(TimeProvider) for testability.
    /// </summary>
    public int AvailableQuantity => GetAvailableQuantity();

    /// <summary>
    ///     Reserved quantity from active reservations.
    ///     Uses system time - prefer GetReservedQuantity(TimeProvider) for testability.
    /// </summary>
    public int ReservedQuantity => GetReservedQuantity();

    public bool IsLowStock => AvailableQuantity <= LowStockThreshold;

    public static Stock Create(
        Guid productId,
        string sku,
        int initialQuantity,
        int lowStockThreshold = 10,
        string? warehouseLocation = null,
        TimeProvider? timeProvider = null)
    {
        if (initialQuantity < 0)
            throw new ArgumentException("Quantity cannot be negative", nameof(initialQuantity));

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return new Stock
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Sku = sku,
            Quantity = initialQuantity,
            LowStockThreshold = lowStockThreshold,
            WarehouseLocation = warehouseLocation,
            LastUpdatedAt = now
        };
    }

    /// <summary>
    ///     Creates a soft reservation that expires in 15 minutes.
    ///     Used during checkout to hold stock temporarily.
    /// </summary>
    public StockReservation Reserve(Guid orderId, int quantity, TimeProvider? timeProvider = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        var tp = timeProvider ?? TimeProvider.System;

        // Clean up expired reservations first
        CleanupExpiredReservations(tp);

        var available = GetAvailableQuantity(tp);
        if (quantity > available)
            throw new InvalidOperationException(
                $"Insufficient stock. Available: {available}, Requested: {quantity}");

        var reservation = StockReservation.Create(Id, orderId, quantity, timeProvider: tp);
        _reservations.Add(reservation);
        LastUpdatedAt = tp.GetUtcNow().UtcDateTime;

        RaiseDomainEvent(new StockReservedDomainEvent(
            Id, ProductId, orderId, quantity, GetAvailableQuantity(tp)));

        if (GetAvailableQuantity(tp) <= LowStockThreshold)
            RaiseDomainEvent(new LowStockAlertDomainEvent(Id, ProductId, Sku, GetAvailableQuantity(tp)));

        return reservation;
    }

    /// <summary>
    ///     Confirms a reservation and deducts from actual stock.
    ///     Called after successful payment.
    /// </summary>
    public void ConfirmReservation(Guid reservationId, TimeProvider? timeProvider = null)
    {
        var tp = timeProvider ?? TimeProvider.System;
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);

        if (reservation is null)
            throw new InvalidOperationException($"Reservation {reservationId} not found");

        if (reservation.Status != ReservationStatus.Active)
            throw new InvalidOperationException($"Reservation is not active. Status: {reservation.Status}");

        reservation.Confirm(tp);
        Quantity -= reservation.Quantity;
        LastUpdatedAt = tp.GetUtcNow().UtcDateTime;

        RaiseDomainEvent(new StockDeductedDomainEvent(
            Id, ProductId, reservation.OrderId, reservation.Quantity, Quantity));
    }

    /// <summary>
    ///     Releases a reservation back to available stock.
    ///     Called when order is cancelled or reservation expires.
    /// </summary>
    public void ReleaseReservation(Guid reservationId, TimeProvider? timeProvider = null)
    {
        var tp = timeProvider ?? TimeProvider.System;
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);

        if (reservation is null)
            return;

        reservation.Release(tp);
        LastUpdatedAt = tp.GetUtcNow().UtcDateTime;

        RaiseDomainEvent(new StockReleasedDomainEvent(
            Id, ProductId, reservation.OrderId, reservation.Quantity, GetAvailableQuantity(tp)));
    }

    /// <summary>
    ///     Adds stock (receiving inventory).
    /// </summary>
    public void AddStock(int quantity, string? reason = null, TimeProvider? timeProvider = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        var tp = timeProvider ?? TimeProvider.System;
        Quantity += quantity;
        LastUpdatedAt = tp.GetUtcNow().UtcDateTime;

        RaiseDomainEvent(new StockAddedDomainEvent(Id, ProductId, quantity, Quantity, reason));
    }

    /// <summary>
    ///     Removes stock directly (adjustments, damage, etc.).
    /// </summary>
    public void RemoveStock(int quantity, string reason, TimeProvider? timeProvider = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        var tp = timeProvider ?? TimeProvider.System;
        var available = GetAvailableQuantity(tp);
        if (quantity > available)
            throw new InvalidOperationException($"Cannot remove more than available. Available: {available}");

        Quantity -= quantity;
        LastUpdatedAt = tp.GetUtcNow().UtcDateTime;

        RaiseDomainEvent(new StockRemovedDomainEvent(Id, ProductId, quantity, Quantity, reason));
    }

    public void UpdateLowStockThreshold(int threshold)
    {
        LowStockThreshold = threshold;
    }

    /// <summary>
    ///     Cleans up expired reservations using the specified time provider.
    /// </summary>
    public void CleanupExpiredReservations(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var expired = _reservations
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .ToList();

        foreach (var reservation in expired)
        {
            reservation.Expire(timeProvider);
            RaiseDomainEvent(new ReservationExpiredDomainEvent(
                Id, ProductId, reservation.OrderId, reservation.Quantity));
        }
    }
}
