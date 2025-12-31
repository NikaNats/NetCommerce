using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Inventory.Domain.Stock;

/// <summary>
/// Stock reservation entity for soft reservations.
/// Expires after 15 minutes if not confirmed.
/// </summary>
public sealed class StockReservation : Entity<Guid>
{
    public static readonly TimeSpan DefaultReservationDuration = TimeSpan.FromMinutes(15);

    public Guid StockId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }

    private StockReservation() { }

    internal static StockReservation Create(
        Guid stockId,
        Guid orderId,
        int quantity,
        TimeSpan? duration = null)
    {
        var now = DateTime.UtcNow;
        return new StockReservation
        {
            Id = Guid.NewGuid(),
            StockId = stockId,
            OrderId = orderId,
            Quantity = quantity,
            CreatedAt = now,
            ExpiresAt = now.Add(duration ?? DefaultReservationDuration),
            Status = ReservationStatus.Active
        };
    }

    internal void Confirm()
    {
        Status = ReservationStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
    }

    internal void Release()
    {
        Status = ReservationStatus.Released;
        ReleasedAt = DateTime.UtcNow;
    }

    internal void Expire()
    {
        Status = ReservationStatus.Expired;
        ReleasedAt = DateTime.UtcNow;
    }
}

public enum ReservationStatus
{
    Active = 0,
    Confirmed = 1,
    Released = 2,
    Expired = 3
}
