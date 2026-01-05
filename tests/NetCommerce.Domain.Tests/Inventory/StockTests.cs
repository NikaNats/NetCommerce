using NetCommerce.Domain.Tests.Fakers;
using NetCommerce.Inventory.Domain.Stock;

namespace NetCommerce.Domain.Tests.Inventory;

public class StockTests
{
    [Fact]
    public void Reserve_WithSufficientStock_ShouldCreateSoftReservation()
    {
        // Arrange
        var stock = StockFaker.Generate(100);
        var orderId = Guid.NewGuid();

        // Act
        var reservation = stock.Reserve(orderId, 10);

        // Assert
        reservation.Status.ShouldBe(ReservationStatus.Active);
        stock.AvailableQuantity.ShouldBe(90);
        stock.DomainEvents.ShouldContain(e => e is StockReservedDomainEvent);
    }

    [Fact]
    public void ConfirmReservation_ShouldDeductFromTotal_AndCloseReservation()
    {
        // Arrange
        var stock = StockFaker.Generate(100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);

        // Act
        stock.ConfirmReservation(reservation.Id);

        // Assert
        stock.Quantity.ShouldBe(90); // Physical deduction
        stock.ReservedQuantity.ShouldBe(0); // Reservation cleared
        reservation.Status.ShouldBe(ReservationStatus.Confirmed);
        stock.DomainEvents.ShouldContain(e => e is StockDeductedDomainEvent);
    }

    [Fact]
    public void ReleaseReservation_ShouldRestoreAvailability()
    {
        // Arrange
        var stock = StockFaker.Generate(100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);

        // Pre-Assert
        stock.AvailableQuantity.ShouldBe(90);

        // Act
        stock.ReleaseReservation(reservation.Id);

        // Assert
        stock.AvailableQuantity.ShouldBe(100); // Back to full
        reservation.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public void Reserve_WithInsufficientStock_ShouldThrow()
    {
        // Arrange
        var stock = StockFaker.Generate(5);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => stock.Reserve(Guid.NewGuid(), 10))
            .Message.ShouldContain("Insufficient stock");
    }

    [Fact]
    public void ConfirmReservation_WhenExpired_ShouldThrow()
    {
        // Arrange
        var stock = StockFaker.Generate(100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);

        // Manually simulate expiration/release via domain logic
        stock.ReleaseReservation(reservation.Id);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() =>
            stock.ConfirmReservation(reservation.Id))
            .Message.ShouldContain("not active");
    }

    [Fact]
    public void IsLowStock_ShouldIncludeReservationsInCalculation()
    {
        // Arrange: 20 items, threshold 10.
        var stock = Stock.Create(Guid.NewGuid(), "SKU", 20, 10);

        // Act: Reserve 15. Available is now 5.
        stock.Reserve(Guid.NewGuid(), 15);

        // Assert: 5 available < 10 threshold -> Low Stock
        stock.IsLowStock.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithNegativeQuantity_ShouldThrow()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() =>
            Stock.Create(Guid.NewGuid(), "SKU", -5, 10))
            .Message.ShouldContain("quantity");
    }

    [Fact]
    public void PS5LaunchScenario_MultipleReservations_ShouldTrackCorrectly()
    {
        // Arrange: Simulate PS5 launch - limited stock, high demand
        var stock = Stock.Create(Guid.NewGuid(), "PS5-001", 100, 20);
        var orderIds = new List<Guid>();

        // Act: Multiple customers reserve stock
        for (int i = 0; i < 10; i++)
        {
            var orderId = Guid.NewGuid();
            orderIds.Add(orderId);
            stock.Reserve(orderId, 5); // Each reserves 5 units
        }

        // Assert: Total reserved should be 50, available should be 50
        stock.ReservedQuantity.ShouldBe(50);
        stock.AvailableQuantity.ShouldBe(50);
        stock.Quantity.ShouldBe(100); // Physical stock unchanged
    }

    [Fact]
    public void Reserve_WithZeroQuantity_ShouldThrow()
    {
        // Arrange
        var stock = StockFaker.Generate(100);

        // Act & Assert
        Should.Throw<ArgumentException>(() =>
            stock.Reserve(Guid.NewGuid(), 0))
            .Message.ShouldContain("quantity");
    }

    [Fact]
    public void IsLowStock_WhenAboveThreshold_ShouldReturnFalse()
    {
        // Arrange: 50 items, threshold 10
        var stock = Stock.Create(Guid.NewGuid(), "SKU", 50, 10);

        // Act: Reserve 30, leaving 20 available
        stock.Reserve(Guid.NewGuid(), 30);

        // Assert: 20 available > 10 threshold -> Not low stock
        stock.IsLowStock.ShouldBeFalse();
    }
}
