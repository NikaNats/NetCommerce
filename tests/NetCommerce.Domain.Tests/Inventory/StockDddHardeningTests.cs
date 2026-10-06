using NetCommerce.Domain.Tests.Fakers;
using NetCommerce.Inventory.Domain.Stock;

namespace NetCommerce.Domain.Tests.Inventory;

/// <summary>
///     Executable examples for the Stock hardening pass:
///     single expiry predicate and threshold guards.
/// </summary>
public class StockDddHardeningTests
{
    [Fact]
    public void UpdateLowStockThreshold_WithNegative_ShouldThrow()
    {
        var stock = StockFaker.Generate();
        Should.Throw<ArgumentOutOfRangeException>(() => stock.UpdateLowStockThreshold(-1));
    }

    [Fact]
    public void Create_WithNegativeThreshold_ShouldThrow()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            Stock.Create(Guid.NewGuid(), "SKU-1", 10, lowStockThreshold: -5));
    }

    [Fact]
    public void ExpiryPolicy_ExpiredActive_DoesNotHoldStock()
    {
        var stock = StockFaker.Generate(10);
        var reservation = stock.Reserve(Guid.NewGuid(), 4);
        var afterExpiry = reservation.ExpiresAt.AddMinutes(1);

        ReservationExpiryPolicy.HoldsStock(reservation, afterExpiry).ShouldBeFalse();
        ReservationExpiryPolicy.IsExpired(reservation, afterExpiry).ShouldBeTrue();

        // Stock with only an expired reservation reports full availability.
        stock.GetAvailableQuantity(TimeProvider.System).ShouldBeLessThanOrEqualTo(10);
    }

    [Fact]
    public void ExpiryPolicy_PendingPayment_HoldsStockRegardlessOfExpiry()
    {
        var stock = StockFaker.Generate(10);
        var reservation = stock.Reserve(Guid.NewGuid(), 4);
        stock.LockReservationForPayment(reservation.Id);

        ReservationExpiryPolicy.HoldsStock(reservation, reservation.ExpiresAt.AddHours(1)).ShouldBeTrue();
        stock.AvailableQuantity.ShouldBe(6);
    }
}
