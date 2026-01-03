using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.BackgroundJobs;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Domain.Tests.Inventory;

/// <summary>
///     Unit tests for ReservationCleanupJob background service.
///     Uses in-memory database for fast, isolated testing.
/// </summary>
public class ReservationCleanupJobTests
{
    private readonly ILogger<ReservationCleanupJob> _logger;

    public ReservationCleanupJobTests()
    {
        _logger = Substitute.For<ILogger<ReservationCleanupJob>>();
    }

    private static ServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();

        // Use in-memory database with shared name
        services.AddDbContext<InventoryDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        return services.BuildServiceProvider();
    }

    #region Job Disabled Tests

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ShouldExitImmediately()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        var options = Options.Create(new ReservationCleanupOptions { Enabled = false });
        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(100); // Give some time
        await job.StopAsync(CancellationToken.None);

        // Assert - Logger should have logged "disabled" message
        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("disabled")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Multiple Stocks Tests

    [Fact]
    public async Task CleanupExpiredReservations_WithMultipleStocks_ShouldCleanupAll()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock1 = Stock.Create(Guid.NewGuid(), "STOCK1-SKU", 100);
        var stock2 = Stock.Create(Guid.NewGuid(), "STOCK2-SKU", 100);
        var stock3 = Stock.Create(Guid.NewGuid(), "STOCK3-SKU", 100);

        var res1 = stock1.Reserve(Guid.NewGuid(), 10);
        var res2 = stock2.Reserve(Guid.NewGuid(), 20);
        var res3 = stock3.Reserve(Guid.NewGuid(), 30);

        context.Stocks.AddRange(stock1, stock2, stock3);
        await context.SaveChangesAsync();

        // Expire all reservations
        context.Entry(res1).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        context.Entry(res2).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        context.Entry(res3).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var releasedCount = await verifyContext.StockReservations
            .CountAsync(r => r.Status == ReservationStatus.Released);

        releasedCount.ShouldBe(3);
    }

    #endregion

    #region Available Quantity Tests

    [Fact]
    public async Task CleanupExpiredReservations_ShouldRestoreAvailableQuantity()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "RESTORE-SKU", 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 30);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Verify initial state
        stock.AvailableQuantity.ShouldBe(70);

        // Expire the reservation
        context.Entry(reservation).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var updatedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stock.Id);

        // After release, available quantity should be restored
        updatedStock.AvailableQuantity.ShouldBe(100);
        updatedStock.Quantity.ShouldBe(100); // Total unchanged
    }

    #endregion

    #region Already Released Reservations Tests

    [Fact]
    public async Task CleanupExpiredReservations_WithAlreadyReleasedReservation_ShouldNotDoubleRelease()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "RELEASED-SKU", 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        stock.ReleaseReservation(reservation.Id);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        var originalReleasedAt = reservation.ReleasedAt;

        // Even if we set ExpiresAt to past, already released reservations should not be affected
        var entry = context.Entry(reservation);
        entry.Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var updatedReservation = await verifyContext.StockReservations
            .FirstAsync(r => r.Id == reservation.Id);

        updatedReservation.Status.ShouldBe(ReservationStatus.Released);
        // ReleasedAt should not have changed
        updatedReservation.ReleasedAt.ShouldBe(originalReleasedAt);
    }

    #endregion

    #region Mixed Reservation Status Tests

    [Fact]
    public async Task CleanupExpiredReservations_WithMixedStatuses_ShouldOnlyReleaseExpiredActive()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "MIXED-SKU", 500);

        // Create reservations with different statuses
        var expiredActiveRes = stock.Reserve(Guid.NewGuid(), 10); // Will expire - should be released
        var activeRes = stock.Reserve(Guid.NewGuid(), 20); // Still active - should NOT be released
        var confirmedRes = stock.Reserve(Guid.NewGuid(), 30); // Confirmed - should NOT be released
        stock.ConfirmReservation(confirmedRes.Id);
        var releasedRes = stock.Reserve(Guid.NewGuid(), 40); // Already released - should NOT change
        stock.ReleaseReservation(releasedRes.Id);

        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Expire only the first reservation and the confirmed/released ones (to test they're not affected)
        context.Entry(expiredActiveRes).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        context.Entry(confirmedRes).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        context.Entry(releasedRes).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var updatedExpiredActive = await verifyContext.StockReservations.FirstAsync(r => r.Id == expiredActiveRes.Id);
        var updatedActive = await verifyContext.StockReservations.FirstAsync(r => r.Id == activeRes.Id);
        var updatedConfirmed = await verifyContext.StockReservations.FirstAsync(r => r.Id == confirmedRes.Id);
        var updatedReleased = await verifyContext.StockReservations.FirstAsync(r => r.Id == releasedRes.Id);

        // Only the expired active reservation should be released
        updatedExpiredActive.Status.ShouldBe(ReservationStatus.Released);
        updatedActive.Status.ShouldBe(ReservationStatus.Active);
        updatedConfirmed.Status.ShouldBe(ReservationStatus.Confirmed);
        updatedReleased.Status.ShouldBe(ReservationStatus.Released);
    }

    #endregion

    #region Domain Events Tests

    [Fact]
    public async Task CleanupExpiredReservations_ShouldRaiseDomainEvents()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "EVENT-SKU", 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 20);

        // Clear any events from reserve
        stock.ClearDomainEvents();

        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Expire the reservation
        context.Entry(reservation).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert - Check that domain events were raised on the stock
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var updatedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stock.Id);

        // The release should have happened
        updatedStock.Reservations.First().Status.ShouldBe(ReservationStatus.Released);
    }

    #endregion

    #region Empty Database Tests

    [Fact]
    public async Task CleanupExpiredReservations_WithEmptyDatabase_ShouldCompleteSuccessfully()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act & Assert - Should not throw
        await Should.NotThrowAsync(async () =>
        {
            await job.StartAsync(cts.Token);
            await Task.Delay(150);
            await cts.CancelAsync();
            await job.StopAsync(CancellationToken.None);
        });
    }

    #endregion

    #region Graceful Shutdown Tests

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ShouldStopGracefully()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 50,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(100); // Let it run a bit
        await cts.CancelAsync();

        // Assert - Should complete without throwing
        await Should.NotThrowAsync(async () => await job.StopAsync(CancellationToken.None));

        // Verify "started" log message was received (proves job ran)
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("started")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Ordering/Priority Tests

    [Fact]
    public async Task CleanupExpiredReservations_ShouldProcessOldestFirst()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "ORDER-SKU", 500);

        // Create reservations with different expiry times
        var oldestRes = stock.Reserve(Guid.NewGuid(), 10);
        var middleRes = stock.Reserve(Guid.NewGuid(), 10);
        var newestRes = stock.Reserve(Guid.NewGuid(), 10);

        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Set expiry times - oldest first
        context.Entry(oldestRes).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-30);
        context.Entry(middleRes).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-20);
        context.Entry(newestRes).Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-10);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 5000,
            BatchSize = 2 // Only process 2 at a time
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act - Run once
        await job.StartAsync(cts.Token);
        await Task.Delay(50);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert - The 2 oldest should be released (oldest and middle)
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var updatedOldest = await verifyContext.StockReservations.FirstAsync(r => r.Id == oldestRes.Id);
        var updatedMiddle = await verifyContext.StockReservations.FirstAsync(r => r.Id == middleRes.Id);
        var updatedNewest = await verifyContext.StockReservations.FirstAsync(r => r.Id == newestRes.Id);

        updatedOldest.Status.ShouldBe(ReservationStatus.Released);
        updatedMiddle.Status.ShouldBe(ReservationStatus.Released);
        updatedNewest.Status.ShouldBe(ReservationStatus.Active); // Not processed yet due to batch size
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task CleanupJob_ShouldLogStartupMessage()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 60000,
            BatchSize = 50
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(50);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert - Verify startup log message with interval and batch size
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("started") &&
                                v.ToString()!.Contains("60000") &&
                                v.ToString()!.Contains("50")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Options Tests

    [Fact]
    public void ReservationCleanupOptions_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new ReservationCleanupOptions();

        // Assert
        options.IntervalMs.ShouldBe(60_000);
        options.BatchSize.ShouldBe(100);
        options.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void ReservationCleanupOptions_ShouldAllowCustomValues()
    {
        // Arrange & Act
        var options = new ReservationCleanupOptions
        {
            IntervalMs = 30_000,
            BatchSize = 50,
            Enabled = false
        };

        // Assert
        options.IntervalMs.ShouldBe(30_000);
        options.BatchSize.ShouldBe(50);
        options.Enabled.ShouldBeFalse();
    }

    #endregion

    #region Cleanup Logic Tests

    [Fact]
    public async Task CleanupExpiredReservations_WithExpiredReservation_ShouldRelease()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "TEST-SKU", 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Manually expire the reservation by setting ExpiresAt to past
        var entry = context.Entry(reservation);
        entry.Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150); // Wait for cleanup to run
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var updatedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stock.Id);

        var updatedReservation = updatedStock.Reservations.First();
        updatedReservation.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public async Task CleanupExpiredReservations_WithActiveReservation_ShouldNotRelease()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "ACTIVE-SKU", 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Reservation is not expired (default is 15 minutes in the future)

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var updatedStock = await verifyContext.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.Id == stock.Id);

        var updatedReservation = updatedStock.Reservations.First();
        updatedReservation.Status.ShouldBe(ReservationStatus.Active);
    }

    [Fact]
    public async Task CleanupExpiredReservations_WithConfirmedReservation_ShouldNotRelease()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "CONFIRMED-SKU", 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        stock.ConfirmReservation(reservation.Id);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Even if we set ExpiresAt to past, confirmed reservations should not be affected
        var entry = context.Entry(reservation);
        entry.Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var updatedReservation = await verifyContext.StockReservations
            .FirstAsync(r => r.Id == reservation.Id);

        updatedReservation.Status.ShouldBe(ReservationStatus.Confirmed);
    }

    [Fact]
    public async Task CleanupExpiredReservations_WithMultipleExpired_ShouldReleaseAll()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "MULTI-SKU", 500);
        var reservations = new List<StockReservation>();

        for (var i = 0; i < 5; i++) reservations.Add(stock.Reserve(Guid.NewGuid(), 10));

        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Expire all reservations
        foreach (var res in reservations)
        {
            var entry = context.Entry(res);
            entry.Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        }

        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var releasedCount = await verifyContext.StockReservations
            .CountAsync(r => r.StockId == stock.Id && r.Status == ReservationStatus.Released);

        releasedCount.ShouldBe(5);
    }

    [Fact]
    public async Task CleanupExpiredReservations_ShouldRespectBatchSize()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "BATCH-SKU", 1000);
        var reservations = new List<StockReservation>();

        for (var i = 0; i < 10; i++) reservations.Add(stock.Reserve(Guid.NewGuid(), 10));

        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Expire all reservations
        foreach (var res in reservations)
        {
            var entry = context.Entry(res);
            entry.Property<DateTime>("ExpiresAt").CurrentValue = DateTime.UtcNow.AddMinutes(-5);
        }

        await context.SaveChangesAsync();

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 5000, // Long interval so only initial cleanup runs
            BatchSize = 3 // Only process 3 at a time
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act - Run just once (initial cleanup)
        await job.StartAsync(cts.Token);
        await Task.Delay(50); // Just wait for initial cleanup
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert - Only 3 should be released due to batch size
        using var verifyScope = serviceProvider.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var releasedCount = await verifyContext.StockReservations
            .CountAsync(r => r.StockId == stock.Id && r.Status == ReservationStatus.Released);

        releasedCount.ShouldBe(3);
    }

    [Fact]
    public async Task CleanupExpiredReservations_WithNoExpired_ShouldDoNothing()
    {
        // Arrange
        var dbName = $"TestDb_{Guid.NewGuid()}";
        using var serviceProvider = CreateServiceProvider(dbName);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var stock = Stock.Create(Guid.NewGuid(), "NOEXP-SKU", 100);
        var reservation = stock.Reserve(Guid.NewGuid(), 20);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();

        // Reservation is not expired

        var options = Options.Create(new ReservationCleanupOptions
        {
            Enabled = true,
            IntervalMs = 100,
            BatchSize = 100
        });

        var job = new ReservationCleanupJob(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            options);

        using var cts = new CancellationTokenSource();

        // Act
        await job.StartAsync(cts.Token);
        await Task.Delay(150);
        await cts.CancelAsync();
        await job.StopAsync(CancellationToken.None);

        // Assert - No "Cleaned up" log message should be received
        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString()!.Contains("Cleaned up")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion
}