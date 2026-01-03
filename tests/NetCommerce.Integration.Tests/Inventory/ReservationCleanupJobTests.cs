using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.BackgroundJobs;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Integration.Tests.Inventory;

/// <summary>
///     Integration tests for ReservationCleanupJob background service.
///     Tests the periodic cleanup of expired stock reservations.
/// </summary>
[Trait("Category", "RequiresDocker")]
public class ReservationCleanupJobTests : IntegrationTestBase
{
    public ReservationCleanupJobTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Available Quantity Restoration Tests

    [Fact]
    public async Task CleanupJob_ShouldRestoreAvailableQuantity()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "RESTORE-001", 100);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var reservation = stock.Reserve(Guid.NewGuid(), 30);
        await context.SaveChangesAsync();

        // Verify available quantity is reduced
        stock.AvailableQuantity.ShouldBe(70);

        // Expire the reservation
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE inventory.stock_reservations
            SET expires_at = @p0
            WHERE id = @p1
            """,
            DateTime.UtcNow.AddMinutes(-5),
            reservation.Id);

        // Act
        await RunCleanupJobAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stock.Id);

        // After release, available quantity should be restored
        // The released reservation no longer counts against available
        updatedStock.AvailableQuantity.ShouldBe(100);
        updatedStock.Quantity.ShouldBe(100); // Total unchanged
    }

    #endregion

    #region Helper Methods

    private async Task RunCleanupJobAsync(
        int intervalMs = 100,
        int batchSize = 100,
        bool enabled = true)
    {
        var services = new ServiceCollection();

        // Register DbContext factory
        services.AddDbContextPool<InventoryDbContext>(options =>
            options.UseNpgsql(Fixture.PostgresConnectionString));

        var serviceProvider = services.BuildServiceProvider();

        var options = Options.Create(new ReservationCleanupOptions
        {
            // Use a large interval so the job only runs its initial cleanup once during tests.
            // This avoids flakiness where the periodic timer ticks and performs extra cleanups.
            IntervalMs = Math.Max(intervalMs, 60_000),
            BatchSize = batchSize,
            Enabled = enabled
        });

        var logger = Substitute.For<ILogger<ReservationCleanupJob>>();

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            options);

        // Determine how many expired active reservations exist before starting.
        // If there are any, wait for the initial cleanup to reduce that count.
        var initialExpiredActiveCount = 0;
        if (enabled)
        {
            await using var probeContext = Fixture.CreateInventoryDbContext();
            var now = DateTime.UtcNow;
            initialExpiredActiveCount = await probeContext.StockReservations
                .CountAsync(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now);
        }

        await job.StartAsync(CancellationToken.None);

        if (enabled)
        {
            if (initialExpiredActiveCount > 0)
            {
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline)
                {
                    await using var probeContext = Fixture.CreateInventoryDbContext();
                    var now = DateTime.UtcNow;
                    var expiredActiveCount = await probeContext.StockReservations
                        .CountAsync(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now);

                    if (expiredActiveCount < initialExpiredActiveCount) break;

                    await Task.Delay(25);
                }
            }
            else
            {
                // Give the initial run a chance to execute even if there's nothing to do.
                await Task.Delay(100);
            }
        }
        else
        {
            // Disabled job should exit immediately; small delay keeps behavior consistent.
            await Task.Delay(25);
        }

        await job.StopAsync(CancellationToken.None);
    }

    #endregion

    #region Cleanup Job Core Behavior Tests

    [Fact]
    public async Task CleanupJob_ShouldReleaseExpiredReservations()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "CLEANUP-001", 100);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Create a reservation that's already expired (using reflection to set ExpiresAt)
        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        await context.SaveChangesAsync();

        // Manually update the reservation to be expired
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE inventory.stock_reservations
            SET expires_at = @p0
            WHERE id = @p1
            """,
            DateTime.UtcNow.AddMinutes(-5),
            reservation.Id);

        // Act - Run the cleanup job
        await RunCleanupJobAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stock.Id);

        var updatedReservation = updatedStock.Reservations.First(r => r.Id == reservation.Id);
        updatedReservation.Status.ShouldBe(ReservationStatus.Released);
        updatedReservation.ReleasedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task CleanupJob_ShouldNotAffectActiveReservations()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "ACTIVE-001", 100);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Create a reservation that's still active (not expired)
        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        await context.SaveChangesAsync();

        // Act - Run the cleanup job
        await RunCleanupJobAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stock.Id);

        var updatedReservation = updatedStock.Reservations.First(r => r.Id == reservation.Id);
        updatedReservation.Status.ShouldBe(ReservationStatus.Active);
        updatedReservation.ReleasedAt.ShouldBeNull();
    }

    [Fact]
    public async Task CleanupJob_ShouldNotAffectConfirmedReservations()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "CONFIRMED-001", 100);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        stock.ConfirmReservation(reservation.Id);
        await context.SaveChangesAsync();

        // Manually set the ExpiresAt to past (shouldn't matter since it's confirmed)
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE inventory.stock_reservations
            SET expires_at = @p0
            WHERE id = @p1
            """,
            DateTime.UtcNow.AddMinutes(-5),
            reservation.Id);

        // Act - Run the cleanup job
        await RunCleanupJobAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedReservation = await verifyContext.StockReservations
            .FirstAsync(r => r.Id == reservation.Id);

        updatedReservation.Status.ShouldBe(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task CleanupJob_ShouldNotAffectAlreadyReleasedReservations()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "RELEASED-001", 100);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        stock.ReleaseReservation(reservation.Id);
        await context.SaveChangesAsync();

        // Capture persisted value to avoid timestamp precision differences between in-memory DateTime and Postgres
        var originalReleasedAt = await context.StockReservations
            .Where(r => r.Id == reservation.Id)
            .Select(r => r.ReleasedAt)
            .SingleAsync();

        // Manually set the ExpiresAt to past
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE inventory.stock_reservations
            SET expires_at = @p0
            WHERE id = @p1
            """,
            DateTime.UtcNow.AddMinutes(-5),
            reservation.Id);

        // Act - Run the cleanup job
        await RunCleanupJobAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedReservation = await verifyContext.StockReservations
            .FirstAsync(r => r.Id == reservation.Id);

        updatedReservation.Status.ShouldBe(ReservationStatus.Released);
        // ReleasedAt should not have changed
        updatedReservation.ReleasedAt.ShouldBe(originalReleasedAt);
    }

    #endregion

    #region Batch Processing Tests

    [Fact]
    public async Task CleanupJob_ShouldRespectBatchSize()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "BATCH-001", 500);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Create 10 reservations
        var reservationIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var reservation = stock.Reserve(Guid.NewGuid(), 10);
            reservationIds.Add(reservation.Id);
        }

        await context.SaveChangesAsync();

        // Expire all reservations
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE inventory.stock_reservations
            SET expires_at = @p0
            WHERE stock_id = @p1
            """,
            DateTime.UtcNow.AddMinutes(-5),
            stock.Id);

        // Act - Run the cleanup job with batch size of 5
        await RunCleanupJobAsync(batchSize: 5);

        // Assert - Only 5 should be released
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var releasedCount = await verifyContext.StockReservations
            .CountAsync(r => r.StockId == stock.Id && r.Status == ReservationStatus.Released);

        releasedCount.ShouldBe(5);

        // Run again to release the remaining
        await RunCleanupJobAsync(batchSize: 5);

        var finalReleasedCount = await verifyContext.StockReservations
            .CountAsync(r => r.StockId == stock.Id && r.Status == ReservationStatus.Released);

        finalReleasedCount.ShouldBe(10);
    }

    [Fact]
    public async Task CleanupJob_WithMultipleStocks_ShouldCleanupAll()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock1 = Stock.Create(Guid.NewGuid(), "MULTI-001", 100);
        var stock2 = Stock.Create(Guid.NewGuid(), "MULTI-002", 100);
        var stock3 = Stock.Create(Guid.NewGuid(), "MULTI-003", 100);

        context.Stocks.AddRange(stock1, stock2, stock3);
        await context.SaveChangesAsync();

        var res1 = stock1.Reserve(Guid.NewGuid(), 10);
        var res2 = stock2.Reserve(Guid.NewGuid(), 20);
        var res3 = stock3.Reserve(Guid.NewGuid(), 30);
        await context.SaveChangesAsync();

        // Expire all reservations
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE inventory.stock_reservations
            SET expires_at = @p0
            """,
            DateTime.UtcNow.AddMinutes(-5));

        // Act
        await RunCleanupJobAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var releasedCount = await verifyContext.StockReservations
            .CountAsync(r => r.Status == ReservationStatus.Released);

        releasedCount.ShouldBe(3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CleanupJob_WithNoExpiredReservations_ShouldDoNothing()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "NOEXP-001", 100);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        await context.SaveChangesAsync();

        // Act - Run the cleanup job (reservation is not expired)
        await RunCleanupJobAsync();

        // Assert
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedReservation = await verifyContext.StockReservations
            .FirstAsync(r => r.Id == reservation.Id);

        updatedReservation.Status.ShouldBe(ReservationStatus.Active);
    }

    [Fact]
    public async Task CleanupJob_WithEmptyDatabase_ShouldCompleteSuccessfully()
    {
        // Arrange - Database is already reset, no stocks or reservations

        // Act & Assert - Should not throw
        await Should.NotThrowAsync(async () => await RunCleanupJobAsync());
    }

    [Fact]
    public async Task CleanupJob_WhenDisabled_ShouldNotProcess()
    {
        // Arrange
        await using var context = Fixture.CreateInventoryDbContext();

        var stock = Stock.Create(Guid.NewGuid(), "DISABLED-001", 100);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        await context.SaveChangesAsync();

        // Expire the reservation
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE inventory.stock_reservations
            SET expires_at = @p0
            WHERE id = @p1
            """,
            DateTime.UtcNow.AddMinutes(-5),
            reservation.Id);

        // Act - Run with job disabled
        await RunCleanupJobAsync(enabled: false);

        // Assert - Reservation should still be active (job didn't run)
        await using var verifyContext = Fixture.CreateInventoryDbContext();
        var updatedReservation = await verifyContext.StockReservations
            .FirstAsync(r => r.Id == reservation.Id);

        // Note: The job exits immediately when disabled, so no cleanup happens
        updatedReservation.Status.ShouldBe(ReservationStatus.Active);
    }

    #endregion
}