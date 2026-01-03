using NetCommerce.SharedKernel.Domain;

namespace NetCommerce.Inventory.Domain.Stock;

/// <summary>
///     Stock reservation entity for soft reservations.
///     Expires after 15 minutes if not confirmed.
///     Uses TimeProvider for deterministic time operations.
/// </summary>
public sealed class StockReservation : Entity<Guid>
{
    public static readonly TimeSpan DefaultReservationDuration = TimeSpan.FromMinutes(15);

    private StockReservation()
    {
    }

    public Guid StockId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }

    internal static StockReservation Create(
        Guid stockId,
        Guid orderId,
        int quantity,
        TimeSpan? duration = null,
        TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
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

    internal void Confirm(TimeProvider? timeProvider = null)
    {
        Status = ReservationStatus.Confirmed;
        ConfirmedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    internal void Release(TimeProvider? timeProvider = null)
    {
        Status = ReservationStatus.Released;
        ReleasedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    internal void Expire(TimeProvider? timeProvider = null)
    {
        Status = ReservationStatus.Expired;
        ReleasedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }
}

public enum ReservationStatus
{
    Active = 0,
    Confirmed = 1,
    Released = 2,
    Expired = 3
}
