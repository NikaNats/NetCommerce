using Shouldly;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Domain.Tests.Fakers;

namespace NetCommerce.Domain.Tests.Inventory;

/// <summary>
/// Unit tests for Stock aggregate - the core of inventory management.
/// Tests soft reservation pattern (15-minute holds).
/// </summary>
public class StockTests
{
    #region Create Tests

    [Fact]
    public void Create_WithValidData_ShouldCreateStock()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var sku = "PS5-001";
        var quantity = 100;
        var threshold = 10;
        var location = "Main Warehouse";

        // Act
        var stock = Stock.Create(productId, sku, quantity, threshold, location);

        // Assert
        stock.ShouldNotBeNull();
        stock.Id.ShouldNotBe(Guid.Empty);
        stock.ProductId.ShouldBe(productId);
        stock.Sku.ShouldBe(sku);
        stock.Quantity.ShouldBe(quantity);
        stock.LowStockThreshold.ShouldBe(threshold);
        stock.WarehouseLocation.ShouldBe(location);
    }

    [Fact]
    public void Create_WithNegativeQuantity_ShouldThrowException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => 
            Stock.Create(Guid.NewGuid(), "SKU", -1))
            .Message.ShouldContain("negative");
    }

    [Fact]
    public void Create_AvailableQuantity_ShouldEqualTotalQuantity()
    {
        // Arrange & Act
        var stock = StockFaker.Generate(quantity: 100);

        // Assert
        stock.AvailableQuantity.ShouldBe(100);
        stock.ReservedQuantity.ShouldBe(0);
    }

    #endregion

    #region Reserve Tests (Soft Reservation - 15 minute holds)

    [Fact]
    public void Reserve_WithSufficientStock_ShouldCreateReservation()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var orderId = Guid.NewGuid();

        // Act
        var reservation = stock.Reserve(orderId, 10);

        // Assert
        reservation.ShouldNotBeNull();
        reservation.OrderId.ShouldBe(orderId);
        reservation.Quantity.ShouldBe(10);
        reservation.Status.ShouldBe(ReservationStatus.Active);
        stock.Reservations.ShouldContain(reservation);
    }

    [Fact]
    public void Reserve_ShouldReduceAvailableQuantity()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);

        // Act
        stock.Reserve(Guid.NewGuid(), 30);

        // Assert
        stock.AvailableQuantity.ShouldBe(70);
        stock.ReservedQuantity.ShouldBe(30);
        stock.Quantity.ShouldBe(100); // Total unchanged
    }

    [Fact]
    public void Reserve_WithInsufficientStock_ShouldThrowException()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 10);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => 
            stock.Reserve(Guid.NewGuid(), 20))
            .Message.ShouldContain("Insufficient stock");
    }

    [Fact]
    public void Reserve_WithZeroQuantity_ShouldThrowException()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);

        // Act & Assert
        Should.Throw<ArgumentException>(() => 
            stock.Reserve(Guid.NewGuid(), 0))
            .Message.ShouldContain("positive");
    }

    [Fact]
    public void Reserve_ShouldRaise_StockReservedDomainEvent()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);

        // Act
        stock.Reserve(Guid.NewGuid(), 10);

        // Assert
        stock.DomainEvents.ShouldContain(e => e is StockReservedDomainEvent);
        
        var reservedEvent = stock.DomainEvents.OfType<StockReservedDomainEvent>().Single();
        reservedEvent.StockId.ShouldBe(stock.Id);
        reservedEvent.Quantity.ShouldBe(10);
        reservedEvent.RemainingAvailable.ShouldBe(90);
    }

    [Fact]
    public void Reserve_WhenResultsInLowStock_ShouldRaise_LowStockAlertDomainEvent()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 15, threshold: 10);

        // Act
        stock.Reserve(Guid.NewGuid(), 10);

        // Assert
        stock.IsLowStock.ShouldBeTrue();
        stock.DomainEvents.ShouldContain(e => e is LowStockAlertDomainEvent);
    }

    [Fact]
    public void Reserve_MultipleOrders_ShouldTrackIndependently()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();

        // Act
        stock.Reserve(order1, 30);
        stock.Reserve(order2, 20);

        // Assert
        stock.Reservations.Count.ShouldBe(2);
        stock.ReservedQuantity.ShouldBe(50);
        stock.AvailableQuantity.ShouldBe(50);
    }

    #endregion

    #region ConfirmReservation Tests

    [Fact]
    public void ConfirmReservation_ShouldDeductFromTotalStock()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);

        // Act
        stock.ConfirmReservation(reservation.Id);

        // Assert
        stock.Quantity.ShouldBe(90); // Deducted from total
        stock.AvailableQuantity.ShouldBe(90);
        stock.ReservedQuantity.ShouldBe(0);
    }

    [Fact]
    public void ConfirmReservation_ShouldChangeStatus_ToConfirmed()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);

        // Act
        stock.ConfirmReservation(reservation.Id);

        // Assert
        reservation.Status.ShouldBe(ReservationStatus.Confirmed);
        reservation.ConfirmedAt.ShouldNotBeNull();
    }

    [Fact]
    public void ConfirmReservation_ShouldRaise_StockDeductedDomainEvent()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var orderId = Guid.NewGuid();
        var reservation = stock.Reserve(orderId, 10);
        stock.ClearDomainEvents();

        // Act
        stock.ConfirmReservation(reservation.Id);

        // Assert
        var deductedEvent = stock.DomainEvents.OfType<StockDeductedDomainEvent>().Single();
        deductedEvent.StockId.ShouldBe(stock.Id);
        deductedEvent.OrderId.ShouldBe(orderId);
        deductedEvent.Quantity.ShouldBe(10);
        deductedEvent.NewTotal.ShouldBe(90);
    }

    [Fact]
    public void ConfirmReservation_WithInvalidId_ShouldThrowException()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => 
            stock.ConfirmReservation(Guid.NewGuid()))
            .Message.ShouldContain("not found");
    }

    [Fact]
    public void ConfirmReservation_WhenAlreadyConfirmed_ShouldThrowException()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);
        stock.ConfirmReservation(reservation.Id);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => 
            stock.ConfirmReservation(reservation.Id))
            .Message.ShouldContain("not active");
    }

    #endregion

    #region ReleaseReservation Tests

    [Fact]
    public void ReleaseReservation_ShouldReturnStockToAvailable()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);
        stock.AvailableQuantity.ShouldBe(90);

        // Act
        stock.ReleaseReservation(reservation.Id);

        // Assert
        stock.AvailableQuantity.ShouldBe(100);
        stock.ReservedQuantity.ShouldBe(0);
        stock.Quantity.ShouldBe(100); // Unchanged
    }

    [Fact]
    public void ReleaseReservation_ShouldChangeStatus_ToReleased()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 10);

        // Act
        stock.ReleaseReservation(reservation.Id);

        // Assert
        reservation.Status.ShouldBe(ReservationStatus.Released);
        reservation.ReleasedAt.ShouldNotBeNull();
    }

    [Fact]
    public void ReleaseReservation_ShouldRaise_StockReleasedDomainEvent()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);
        var orderId = Guid.NewGuid();
        var reservation = stock.Reserve(orderId, 10);
        stock.ClearDomainEvents();

        // Act
        stock.ReleaseReservation(reservation.Id);

        // Assert
        var releasedEvent = stock.DomainEvents.OfType<StockReleasedDomainEvent>().Single();
        releasedEvent.StockId.ShouldBe(stock.Id);
        releasedEvent.OrderId.ShouldBe(orderId);
        releasedEvent.Quantity.ShouldBe(10);
    }

    [Fact]
    public void ReleaseReservation_WithInvalidId_ShouldNotThrow()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100);

        // Act & Assert (should be idempotent)
        Should.NotThrow(() => stock.ReleaseReservation(Guid.NewGuid()));
    }

    #endregion

    #region Low Stock Threshold Tests

    [Fact]
    public void IsLowStock_WhenBelowThreshold_ShouldReturnTrue()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 10, threshold: 10);

        // Assert
        stock.IsLowStock.ShouldBeTrue();
    }

    [Fact]
    public void IsLowStock_WhenAboveThreshold_ShouldReturnFalse()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 100, threshold: 10);

        // Assert
        stock.IsLowStock.ShouldBeFalse();
    }

    [Fact]
    public void IsLowStock_ShouldConsiderReservations()
    {
        // Arrange
        var stock = StockFaker.Generate(quantity: 20, threshold: 10);
        stock.IsLowStock.ShouldBeFalse();

        // Act
        stock.Reserve(Guid.NewGuid(), 15);

        // Assert - Available is now 5, below threshold of 10
        stock.AvailableQuantity.ShouldBe(5);
        stock.IsLowStock.ShouldBeTrue();
    }

    #endregion

    #region Concurrent Reservation Scenario (PS5 Launch Simulation)

    [Fact]
    public void PS5LaunchScenario_MultipleReservations_ShouldTrackCorrectly()
    {
        // Arrange - Limited PS5 stock
        var ps5Stock = Stock.Create(
            productId: Guid.NewGuid(),
            sku: "PS5-DIGITAL-2024",
            initialQuantity: 5, // Only 5 PS5s!
            lowStockThreshold: 2,
            warehouseLocation: "Main DC");

        var customers = Enumerable.Range(1, 5)
            .Select(_ => Guid.NewGuid())
            .ToList();

        // Act - All 5 customers successfully reserve
        var reservations = customers
            .Select(c => ps5Stock.Reserve(c, 1))
            .ToList();

        // Assert
        ps5Stock.AvailableQuantity.ShouldBe(0);
        ps5Stock.ReservedQuantity.ShouldBe(5);
        reservations.Count.ShouldBe(5);
        reservations.All(r => r.Status == ReservationStatus.Active).ShouldBeTrue();

        // 6th customer should fail
        Should.Throw<InvalidOperationException>(() => 
            ps5Stock.Reserve(Guid.NewGuid(), 1))
            .Message.ShouldContain("Insufficient stock");
    }

    [Fact]
    public void PS5LaunchScenario_ReservationExpiry_ShouldReleaseStock()
    {
        // Arrange
        var ps5Stock = Stock.Create(Guid.NewGuid(), "PS5", 2, 1);
        
        // Customer 1 reserves
        var reservation1 = ps5Stock.Reserve(Guid.NewGuid(), 1);
        
        // Customer 2 reserves
        var reservation2 = ps5Stock.Reserve(Guid.NewGuid(), 1);
        
        ps5Stock.AvailableQuantity.ShouldBe(0);

        // Customer 1 abandons cart (reservation released)
        ps5Stock.ReleaseReservation(reservation1.Id);

        // Assert - Stock is available again
        ps5Stock.AvailableQuantity.ShouldBe(1);
        
        // Customer 3 can now reserve
        var reservation3 = ps5Stock.Reserve(Guid.NewGuid(), 1);
        reservation3.ShouldNotBeNull();
    }

    [Fact]
    public void PS5LaunchScenario_ConfirmPurchase_ShouldDeductPermanently()
    {
        // Arrange
        var ps5Stock = Stock.Create(Guid.NewGuid(), "PS5", 3, 1);
        
        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();
        
        var reservation1 = ps5Stock.Reserve(order1, 1);
        var reservation2 = ps5Stock.Reserve(order2, 1);

        // Act - Customer 1 completes purchase
        ps5Stock.ConfirmReservation(reservation1.Id);

        // Assert
        ps5Stock.Quantity.ShouldBe(2); // Permanently reduced
        ps5Stock.AvailableQuantity.ShouldBe(1); // 2 total - 1 reserved
        ps5Stock.ReservedQuantity.ShouldBe(1); // reservation2 still active
    }

    #endregion
}
