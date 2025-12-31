using Shouldly;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Integration.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace NetCommerce.Integration.Tests.Inventory;

/// <summary>
/// Integration tests for Inventory module with focus on stock reservations.
/// Uses Testcontainers PostgreSQL with Respawn for database cleanup.
/// </summary>
[Trait("Category", "RequiresDocker")]
public class InventoryRepositoryTests : IntegrationTestBase
{
    public InventoryRepositoryTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Stock CRUD Tests

    [Fact]
    public async Task AddStock_ShouldPersistToDatabase()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(
            productId: Guid.NewGuid(),
            sku: "PS5-001",
            initialQuantity: 100,
            lowStockThreshold: 10,
            warehouseLocation: "Main Warehouse");

        // Act
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var savedStock = await verifyContext.Stocks.FindAsync(stock.Id);

        savedStock.ShouldNotBeNull();
        savedStock.Sku.ShouldBe("PS5-001");
        savedStock.Quantity.ShouldBe(100);
        savedStock.LowStockThreshold.ShouldBe(10);
    }

    [Fact]
    public async Task UpdateStock_ShouldPersistChanges()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(Guid.NewGuid(), "STOCK-001", 50, 5);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Act - Add more stock
        stock.AddStock(25, "Restocking");
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedStock = await verifyContext.Stocks.FindAsync(stock.Id);

        updatedStock.ShouldNotBeNull();
        updatedStock.Quantity.ShouldBe(75);
    }

    #endregion

    #region Reservation Persistence Tests

    [Fact]
    public async Task Reserve_ShouldPersistReservation()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(Guid.NewGuid(), "RES-001", 100, 10);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var orderId = Guid.NewGuid();

        // Act
        stock.Reserve(orderId, 10);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var savedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.Id == stock.Id);

        savedStock.ShouldNotBeNull();
        savedStock.Reservations.Count.ShouldBe(1);
        
        var reservation = savedStock.Reservations.First();
        reservation.OrderId.ShouldBe(orderId);
        reservation.Quantity.ShouldBe(10);
        reservation.Status.ShouldBe(ReservationStatus.Active);
    }

    [Fact]
    public async Task ConfirmReservation_ShouldUpdateStatusAndDeductStock()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(Guid.NewGuid(), "CONF-001", 50, 5);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var reservation = stock.Reserve(Guid.NewGuid(), 10);
        await context.SaveChangesAsync();
        var reservationId = reservation.Id;

        // Act
        stock.ConfirmReservation(reservationId);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var savedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.Id == stock.Id);

        savedStock.ShouldNotBeNull();
        savedStock.Quantity.ShouldBe(40); // 50 - 10
        
        var confirmedReservation = savedStock.Reservations.First(r => r.Id == reservationId);
        confirmedReservation.Status.ShouldBe(ReservationStatus.Confirmed);
        confirmedReservation.ConfirmedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ReleaseReservation_ShouldUpdateStatus()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(Guid.NewGuid(), "REL-001", 100, 10);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        await context.SaveChangesAsync();
        var reservationId = reservation.Id;

        // Act
        stock.ReleaseReservation(reservationId);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var savedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.Id == stock.Id);

        savedStock.ShouldNotBeNull();
        savedStock.Quantity.ShouldBe(100); // Unchanged
        
        var releasedReservation = savedStock.Reservations.First(r => r.Id == reservationId);
        releasedReservation.Status.ShouldBe(ReservationStatus.Released);
        releasedReservation.ReleasedAt.ShouldNotBeNull();
    }

    #endregion

    #region Multiple Reservations Tests

    [Fact]
    public async Task MultipleReservations_ShouldTrackIndependently()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(Guid.NewGuid(), "MULTI-001", 100, 10);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();
        var order3 = Guid.NewGuid();

        // Act
        var res1 = stock.Reserve(order1, 30);
        var res2 = stock.Reserve(order2, 20);
        var res3 = stock.Reserve(order3, 10);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var savedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstOrDefaultAsync(s => s.Id == stock.Id);

        savedStock.ShouldNotBeNull();
        savedStock.Reservations.Count.ShouldBe(3);
        savedStock.ReservedQuantity.ShouldBe(60);
        savedStock.AvailableQuantity.ShouldBe(40);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task QueryStock_ByProductId_ShouldReturnStock()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var productId = Guid.NewGuid();
        var stock = Stock.Create(productId, "QUERY-001", 50, 5);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Act
        var foundStock = await context.Stocks
            .FirstOrDefaultAsync(s => s.ProductId == productId);

        // Assert
        foundStock.ShouldNotBeNull();
        foundStock.Sku.ShouldBe("QUERY-001");
    }

    [Fact]
    public async Task QueryStock_LowStock_ShouldReturnBelowThreshold()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        context.Stocks.AddRange(
            Stock.Create(Guid.NewGuid(), "HIGH-001", 100, 10),
            Stock.Create(Guid.NewGuid(), "LOW-001", 5, 10),
            Stock.Create(Guid.NewGuid(), "LOW-002", 8, 10));
        
        await context.SaveChangesAsync();

        // Act
        var lowStockItems = await context.Stocks
            .Where(s => s.Quantity <= s.LowStockThreshold)
            .ToListAsync();

        // Assert
        lowStockItems.Count.ShouldBe(2);
        lowStockItems.All(s => s.Sku.StartsWith("LOW")).ShouldBeTrue();
    }

    [Fact]
    public async Task QueryStock_WithActiveReservations_ShouldIncludeReservations()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(Guid.NewGuid(), "ACTIVE-001", 100, 10);
        stock.Reserve(Guid.NewGuid(), 10);
        stock.Reserve(Guid.NewGuid(), 20);
        
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Act
        var stockWithReservations = await context.Stocks
            .Include(s => s.Reservations.Where(r => r.Status == ReservationStatus.Active))
            .FirstOrDefaultAsync(s => s.Id == stock.Id);

        // Assert
        stockWithReservations.ShouldNotBeNull();
        stockWithReservations.Reservations.Count.ShouldBe(2);
    }

    #endregion

    #region Concurrency Tests (Critical for PS5 Scenario)

    [Fact]
    public async Task ConcurrentReservations_WithOptimisticLocking_ShouldDetectConflicts()
    {
        // Arrange - Limited stock scenario
        await using var context1 = Fixture.CreateInventoryDbContext();
        
        var stock = Stock.Create(Guid.NewGuid(), "CONC-PS5", 2, 1);
        context1.Stocks.Add(stock);
        await context1.SaveChangesAsync();
        var stockId = stock.Id;

        // Act - Two contexts try to reserve simultaneously
        await using var context2 = Fixture.CreateInventoryDbContext();
        
        var stock1 = await context1.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stockId);
        var stock2 = await context2.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stockId);

        // Both try to reserve
        stock1.Reserve(Guid.NewGuid(), 1);
        stock2.Reserve(Guid.NewGuid(), 1);

        // First save succeeds
        await context1.SaveChangesAsync();

        // Second save should detect concurrency conflict
        await Should.ThrowAsync<DbUpdateConcurrencyException>(async () =>
        {
            await context2.SaveChangesAsync();
        });
    }

    #endregion
}
