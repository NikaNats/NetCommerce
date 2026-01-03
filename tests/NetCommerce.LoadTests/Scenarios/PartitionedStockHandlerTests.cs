using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.Handlers;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.LoadTests.Fixtures;
using NetCommerce.SharedKernel.Events;
using NSubstitute;
using Shouldly;

namespace NetCommerce.LoadTests.Scenarios;

/// <summary>
///     Unit tests for Partitioned Sequential Messaging handlers.
///     Tests the inventory handlers that use message partitioning for high-contention scenarios.
///     Uses real PostgreSQL (via Testcontainers) to accurately test row-level locking and transactions.
/// </summary>
/// <remarks>
///     IMPORTANT: Using InMemoryDatabase for concurrency tests produces false positives since
///     it doesn't properly simulate row-level locking and transaction isolation.
///     Real PostgreSQL ensures tests catch actual concurrency issues.
/// </remarks>
[Collection(nameof(PostgresTestCollection))]
[Trait("Category", "RequiresDocker")]
public class PartitionedStockHandlerTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private InventoryDbContext _dbContext = null!;
    private readonly ILogger<PartitionedReserveInventoryHandler> _reserveLogger;
    private readonly ILogger<PartitionedConfirmInventoryHandler> _confirmLogger;
    private readonly ILogger<PartitionedReleaseInventoryHandler> _releaseLogger;

    public PartitionedStockHandlerTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
        _reserveLogger = Substitute.For<ILogger<PartitionedReserveInventoryHandler>>();
        _confirmLogger = Substitute.For<ILogger<PartitionedConfirmInventoryHandler>>();
        _releaseLogger = Substitute.For<ILogger<PartitionedReleaseInventoryHandler>>();
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _dbContext = _fixture.CreateInventoryDbContext();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    #region ReserveInventoryCommand Tests

    [Fact]
    public async Task Handle_ReserveInventory_WithSufficientStock_ShouldSucceed()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var stock = Stock.Create(productId, "PS5-CONSOLE", 100, 10);
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand(
            orderId,
            [new OrderItemReservation(productId, 1, "PS5-CONSOLE")]);

        // Act
        var result = await PartitionedReserveInventoryHandler.Handle(
            command, _dbContext, _reserveLogger, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<InventoryReserved>();
        var reserved = (InventoryReserved)result;
        reserved.OrderId.ShouldBe(orderId);
        reserved.ReservedItems.Count.ShouldBe(1);
        reserved.ReservedItems[0].ProductId.ShouldBe(productId);
        reserved.ReservedItems[0].Quantity.ShouldBe(1);

        // Verify stock was updated
        var updatedStock = await _dbContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);
        updatedStock.AvailableQuantity.ShouldBe(99);
        updatedStock.ReservedQuantity.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ReserveInventory_WithInsufficientStock_ShouldFail()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var stock = Stock.Create(productId, "PS5-CONSOLE", 5, 1);
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand(
            orderId,
            [new OrderItemReservation(productId, 10, "PS5-CONSOLE")]);

        // Act
        var result = await PartitionedReserveInventoryHandler.Handle(
            command, _dbContext, _reserveLogger, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<InventoryReservationFailed>();
        var failed = (InventoryReservationFailed)result;
        failed.OrderId.ShouldBe(orderId);
        failed.UnavailableProductIds.ShouldNotBeNull();
        failed.UnavailableProductIds.ShouldContain(productId);
    }

    [Fact]
    public async Task Handle_ReserveInventory_WithNonexistentProduct_ShouldFail()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var command = new ReserveInventoryCommand(
            orderId,
            [new OrderItemReservation(productId, 1, "NONEXISTENT")]);

        // Act
        var result = await PartitionedReserveInventoryHandler.Handle(
            command, _dbContext, _reserveLogger, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<InventoryReservationFailed>();
        var failed = (InventoryReservationFailed)result;
        failed.UnavailableProductIds.ShouldContain(productId);
    }

    [Fact]
    public async Task Handle_ReserveInventory_WithEmptyItems_ShouldFail()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var command = new ReserveInventoryCommand(orderId, []);

        // Act
        var result = await PartitionedReserveInventoryHandler.Handle(
            command, _dbContext, _reserveLogger, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<InventoryReservationFailed>();
        var failed = (InventoryReservationFailed)result;
        failed.Reason.ShouldContain("No items");
    }

    [Fact]
    public async Task Handle_ReserveInventory_MultipleItems_ShouldReserveAll()
    {
        // Arrange
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        _dbContext.Stocks.AddRange(
            Stock.Create(productId1, "PS5-CONSOLE", 50),
            Stock.Create(productId2, "XBOX-SERIES-X", 50));
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand(orderId,
        [
            new OrderItemReservation(productId1, 2, "PS5-CONSOLE"),
            new OrderItemReservation(productId2, 3, "XBOX-SERIES-X")
        ]);

        // Act
        var result = await PartitionedReserveInventoryHandler.Handle(
            command, _dbContext, _reserveLogger, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<InventoryReserved>();
        var reserved = (InventoryReserved)result;
        reserved.ReservedItems.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_ReserveInventory_PartialAvailability_ShouldFailAll()
    {
        // Arrange - One product has enough stock, one doesn't
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        _dbContext.Stocks.AddRange(
            Stock.Create(productId1, "PS5-CONSOLE", 50),
            Stock.Create(productId2, "XBOX-SERIES-X", 1)); // Only 1 available
        await _dbContext.SaveChangesAsync();

        var command = new ReserveInventoryCommand(orderId,
        [
            new OrderItemReservation(productId1, 2, "PS5-CONSOLE"),
            new OrderItemReservation(productId2, 10, "XBOX-SERIES-X") // Requesting 10
        ]);

        // Act
        var result = await PartitionedReserveInventoryHandler.Handle(
            command, _dbContext, _reserveLogger, CancellationToken.None);

        // Assert - Should fail entirely, not partial
        result.ShouldBeOfType<InventoryReservationFailed>();
        var failed = (InventoryReservationFailed)result;
        failed.UnavailableProductIds.ShouldContain(productId2);
    }

    #endregion

    #region ConfirmInventoryCommand Tests

    [Fact]
    public async Task Handle_ConfirmInventory_WithActiveReservation_ShouldSucceed()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var stock = Stock.Create(productId, "PS5-CONSOLE", 100);
        var reservation = stock.Reserve(orderId, 5);
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();

        var command = new ConfirmInventoryCommand(orderId, transactionId);

        // Act
        var result = await PartitionedConfirmInventoryHandler.Handle(
            command, _dbContext, _confirmLogger, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<InventoryConfirmed>();
        var confirmed = (InventoryConfirmed)result;
        confirmed.OrderId.ShouldBe(orderId);

        // Verify reservation was confirmed
        var updatedStock = await _dbContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        updatedStock.Quantity.ShouldBe(95); // Stock was deducted
        var confirmedReservation = updatedStock.Reservations.First();
        confirmedReservation.Status.ShouldBe(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task Handle_ConfirmInventory_WithNoReservation_ShouldFail()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var command = new ConfirmInventoryCommand(orderId, transactionId);

        // Act
        var result = await PartitionedConfirmInventoryHandler.Handle(
            command, _dbContext, _confirmLogger, CancellationToken.None);

        // Assert
        result.ShouldBeOfType<InventoryConfirmationFailed>();
        var failed = (InventoryConfirmationFailed)result;
        failed.Reason.ShouldContain("No reservations found");
    }

    #endregion

    #region ReleaseInventoryReservationCommand Tests

    [Fact]
    public async Task Handle_ReleaseInventory_WithActiveReservation_ShouldSucceed()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var stock = Stock.Create(productId, "PS5-CONSOLE", 100);
        stock.Reserve(orderId, 10);
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();

        var command = new ReleaseInventoryReservationCommand(orderId, "Payment failed");

        // Act
        await PartitionedReleaseInventoryHandler.Handle(
            command, _dbContext, _releaseLogger, CancellationToken.None);

        // Assert - Stock should be available again
        var updatedStock = await _dbContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        updatedStock.AvailableQuantity.ShouldBe(100); // Full stock available
        var releasedReservation = updatedStock.Reservations.First();
        releasedReservation.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public async Task Handle_ReleaseInventory_WithNoReservation_ShouldNotThrow()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var command = new ReleaseInventoryReservationCommand(orderId, "Cleanup");

        // Act & Assert - Should not throw
        await PartitionedReleaseInventoryHandler.Handle(
            command, _dbContext, _releaseLogger, CancellationToken.None);
    }

    #endregion

    #region Sequential Processing Simulation Tests

    /// <summary>
    ///     Simulates the sequential processing behavior of partitioned messaging.
    ///     All requests for the same ProductId are processed one at a time.
    /// </summary>
    [Fact]
    public async Task SequentialProcessing_SameProduct_ShouldNotOversell()
    {
        // Arrange - Limited PS5 stock
        const int totalStock = 10;
        const int requestCount = 50;
        var productId = Guid.NewGuid();

        var stock = Stock.Create(productId, "PS5-CONSOLE", totalStock, 1);
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();

        var successCount = 0;
        var failCount = 0;

        // Act - Process requests sequentially (as partitioned messaging would do)
        for (var i = 0; i < requestCount; i++)
        {
            var orderId = Guid.NewGuid();
            var command = new ReserveInventoryCommand(orderId,
                [new OrderItemReservation(productId, 1, "PS5-CONSOLE")]);

            // Clear change tracker to simulate fresh context per request
            _dbContext.ChangeTracker.Clear();

            var result = await PartitionedReserveInventoryHandler.Handle(
                command, _dbContext, _reserveLogger, CancellationToken.None);

            if (result is InventoryReserved)
            {
                await _dbContext.SaveChangesAsync();
                successCount++;
            }
            else
            {
                failCount++;
            }
        }

        // Assert - No overselling!
        successCount.ShouldBe(totalStock);
        failCount.ShouldBe(requestCount - totalStock);
    }

    /// <summary>
    ///     Tests the workflow: Reserve → Confirm → Verify stock deducted.
    /// </summary>
    [Fact]
    public async Task FullWorkflow_Reserve_Confirm_ShouldDeductStock()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var stock = Stock.Create(productId, "PS5-CONSOLE", 100);
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();

        // Act - Step 1: Reserve
        var reserveCommand = new ReserveInventoryCommand(orderId,
            [new OrderItemReservation(productId, 5, "PS5-CONSOLE")]);
        var reserveResult = await PartitionedReserveInventoryHandler.Handle(
            reserveCommand, _dbContext, _reserveLogger, CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        reserveResult.ShouldBeOfType<InventoryReserved>();

        // Verify state after reservation
        _dbContext.ChangeTracker.Clear();
        var stockAfterReserve = await _dbContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);
        stockAfterReserve.Quantity.ShouldBe(100); // Not yet deducted
        stockAfterReserve.AvailableQuantity.ShouldBe(95); // 5 reserved
        stockAfterReserve.ReservedQuantity.ShouldBe(5);

        // Act - Step 2: Confirm
        var confirmCommand = new ConfirmInventoryCommand(orderId, transactionId);
        var confirmResult = await PartitionedConfirmInventoryHandler.Handle(
            confirmCommand, _dbContext, _confirmLogger, CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        confirmResult.ShouldBeOfType<InventoryConfirmed>();

        // Verify state after confirmation
        _dbContext.ChangeTracker.Clear();
        var stockAfterConfirm = await _dbContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);
        stockAfterConfirm.Quantity.ShouldBe(95); // Deducted!
        stockAfterConfirm.AvailableQuantity.ShouldBe(95); // No more reserved
        stockAfterConfirm.ReservedQuantity.ShouldBe(0);
    }

    /// <summary>
    ///     Tests the compensating workflow: Reserve → Release → Verify stock restored.
    /// </summary>
    [Fact]
    public async Task FullWorkflow_Reserve_Release_ShouldRestoreStock()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var stock = Stock.Create(productId, "PS5-CONSOLE", 100);
        _dbContext.Stocks.Add(stock);
        await _dbContext.SaveChangesAsync();

        // Act - Step 1: Reserve
        var reserveCommand = new ReserveInventoryCommand(orderId,
            [new OrderItemReservation(productId, 5, "PS5-CONSOLE")]);
        await PartitionedReserveInventoryHandler.Handle(
            reserveCommand, _dbContext, _reserveLogger, CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        // Verify state after reservation
        _dbContext.ChangeTracker.Clear();
        var stockAfterReserve = await _dbContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);
        stockAfterReserve.AvailableQuantity.ShouldBe(95);

        // Act - Step 2: Release (payment failed)
        var releaseCommand = new ReleaseInventoryReservationCommand(orderId, "Payment failed");
        await PartitionedReleaseInventoryHandler.Handle(
            releaseCommand, _dbContext, _releaseLogger, CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        // Verify state after release
        _dbContext.ChangeTracker.Clear();
        var stockAfterRelease = await _dbContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);
        stockAfterRelease.Quantity.ShouldBe(100); // Not deducted
        stockAfterRelease.AvailableQuantity.ShouldBe(100); // Fully available again
        stockAfterRelease.ReservedQuantity.ShouldBe(0);
    }

    #endregion
}
