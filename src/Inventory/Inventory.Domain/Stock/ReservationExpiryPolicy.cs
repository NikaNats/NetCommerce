namespace NetCommerce.Inventory.Domain.Stock;

/// <summary>
///     Single source of truth for "does this reservation still hold stock?".
///     Active + not-expired reservations and payment locks hold stock;
///     Confirmed/Released/Expired (or expired Active) do not.
/// </summary>
public static class ReservationExpiryPolicy
{
    public static bool HoldsStock(StockReservation reservation, DateTime now) =>
        reservation.Status == ReservationStatus.PendingPayment ||
        (reservation.Status == ReservationStatus.Active && reservation.ExpiresAt > now);

    public static bool IsExpired(StockReservation reservation, DateTime now) =>
        reservation.Status == ReservationStatus.Active && reservation.ExpiresAt <= now;
}
