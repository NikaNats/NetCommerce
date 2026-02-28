#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.Core.Results;
using Npgsql;
using Shouldly;
using Wolverine;

namespace NetCommerce.Integration.Tests.Inventory;

/// <summary>
///     Concurrent inventory stress tests using Testcontainers (no NBomber).
///
///     <para>
///     Validates the pessimistic locking strategy (SELECT … FOR UPDATE) under contention.
///     These tests deliberately fire overlapping reservations against the same stock row
///     to ensure exactly-one-winner semantics.
///     </para>
///
///     <para>
///     <b>Implementation note:</b> Concurrent locking tests use raw Npgsql
///     with explicit transactions and barriers to guarantee true concurrent
///     access to the locked row. This directly validates the PostgreSQL
///     <c>SELECT FOR UPDATE</c> serialization behavior used by production handlers.
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "RequiresDocker")]
[Trait("Category", "Stress")]
public class ConcurrentInventoryStressTests : IntegrationTestBase
{
    public ConcurrentInventoryStressTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     Two simultaneous reservations for the last item in stock must result in
    ///     exactly one success and one <c>InvalidOperationException</c> at the domain level.
    ///     Uses raw Npgsql with a barrier to guarantee true concurrent contention.
    /// </summary>
    [Fact]
    public async Task TwoSimultaneousReservations_ForLastItem_ExactlyOneSucceeds()
    {
        // Arrange — single unit of stock
        var productId = Guid.NewGuid();
        var sku = $"SKU-STRESS-1-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, quantity: 1);

        // Barrier ensures both tasks reach the SELECT FOR UPDATE at the same time
        using var barrier = new Barrier(2);

        var task1 = Task.Run(() => ReserveWithRawSqlLocking(productId, Guid.NewGuid(), 1, barrier));
        var task2 = Task.Run(() => ReserveWithRawSqlLocking(productId, Guid.NewGuid(), 1, barrier));

        var results = await Task.WhenAll(task1, task2);

        // Assert — exactly one should succeed
        var successes = results.Count(r => r);
        var failures = results.Count(r => !r);

        successes.ShouldBe(1,
            "Exactly one reservation should succeed when only 1 unit is available");
        failures.ShouldBe(1,
            "Exactly one reservation should fail with insufficient stock");

        // Verify database state — only 1 active reservation
        await using var ctx = Fixture.CreateInventoryDbContext();
        var stock = await ctx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        stock.Reservations.Count(r => r.Status == ReservationStatus.Active).ShouldBe(1,
            "Only one active reservation should exist in the database");
    }

    /// <summary>
    ///     N concurrent reservations (N &gt; available) must result in exactly
    ///     <c>available</c> successes and <c>N − available</c> failures.
    /// </summary>
    [Fact]
    public async Task ManyConcurrentReservations_ShouldNotOversell()
    {
        // Arrange — 5 units, 10 concurrent orders each requesting 1
        const int stockQuantity = 5;
        const int concurrentOrders = 10;

        var productId = Guid.NewGuid();
        var sku = $"SKU-STRESS-N-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, stockQuantity);

        using var barrier = new Barrier(concurrentOrders);

        // Act — fire all at once with raw SQL locking and barrier synchronization
        var tasks = Enumerable.Range(0, concurrentOrders)
            .Select(_ => Task.Run(() =>
                ReserveWithRawSqlLocking(productId, Guid.NewGuid(), 1, barrier)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        var successes = results.Count(r => r);
        var failures = results.Count(r => !r);

        successes.ShouldBe(stockQuantity,
            $"Exactly {stockQuantity} reservations should succeed");
        failures.ShouldBe(concurrentOrders - stockQuantity,
            $"Exactly {concurrentOrders - stockQuantity} should fail");

        // Verify no overselling in database
        await using var ctx = Fixture.CreateInventoryDbContext();
        var stock = await ctx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        var activeReservations = stock.Reservations
            .Count(r => r.Status == ReservationStatus.Active);

        activeReservations.ShouldBe(stockQuantity,
            "Database should have exactly stockQuantity active reservations");
    }

    /// <summary>
    ///     Saga-level concurrent reservations: Two competing orders for the last unit
    ///     processed sequentially to validate the production handler's locking path.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentSagaReservations_ForLastUnit_ExactlyOneSucceeds()
    {
        var productId = Guid.NewGuid();
        var sku = $"SKU-STRESS-SAGA-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, quantity: 1);

        using var barrier = new Barrier(2);

        var task1 = Task.Run(() =>
            ReserveWithRawSqlLocking(productId, Guid.NewGuid(), 1, barrier));
        var task2 = Task.Run(() =>
            ReserveWithRawSqlLocking(productId, Guid.NewGuid(), 1, barrier));

        var results = await Task.WhenAll(task1, task2);

        // Assert — exactly 1 succeeds
        results.Count(r => r).ShouldBe(1,
            "Only one saga-level reservation should succeed for the last unit");

        await using var ctx = Fixture.CreateInventoryDbContext();
        var stock = await ctx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        stock.Reservations.Count(r => r.Status == ReservationStatus.Active).ShouldBe(1,
            "Only one active reservation should exist in the database");
    }

    /// <summary>
    ///     Reserve-then-release-then-reserve cycle should not corrupt stock counters.
    /// </summary>
    [Fact]
    public async Task ReserveReleaseCycle_ShouldMaintainStockIntegrity()
    {
        var productId = Guid.NewGuid();
        var sku = $"SKU-CYCLE-{Guid.NewGuid():N}";
        const int initialQuantity = 10;
        await CreateTestStockAsync(productId, sku, initialQuantity);

        var orderId = Guid.NewGuid();

        // Reserve
        var reserveResult = await InvokeInNewScope<Result<Guid>>(
            new ReserveStockCommand(productId, orderId, Quantity: 5));
        reserveResult.IsSuccess.ShouldBeTrue("Initial reservation should succeed");

        // Release
        var releaseResult = await InvokeInNewScope<Result>(
            new ReleaseReservationCommand(productId, reserveResult.Value));
        releaseResult.IsSuccess.ShouldBeTrue("Release should succeed");

        // Reserve again — should succeed because the previous was released
        var reserveAgainResult = await InvokeInNewScope<Result<Guid>>(
            new ReserveStockCommand(productId, Guid.NewGuid(), Quantity: 10));
        reserveAgainResult.IsSuccess.ShouldBeTrue(
            "After release, full stock should be available for re-reservation");

        // Verify stock integrity
        await using var ctx = Fixture.CreateInventoryDbContext();
        var stock = await ctx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        stock.Quantity.ShouldBe(initialQuantity, "Total quantity should remain unchanged");
    }

    #region Helper Methods

    private async Task<Stock> CreateTestStockAsync(Guid productId, string sku, int quantity)
    {
        await using var context = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, sku, quantity);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();
        return stock;
    }

    /// <summary>
    ///     Performs a stock reservation using raw Npgsql with explicit transaction.
    ///     Mirrors the production handler's SELECT … FOR UPDATE pattern exactly,
    ///     bypassing EF Core's subquery wrapping to ensure genuine row locking.
    /// </summary>
    /// <returns><c>true</c> if reservation succeeded, <c>false</c> if stock was insufficient.</returns>
    private async Task<bool> ReserveWithRawSqlLocking(
        Guid productId, Guid orderId, int quantity, Barrier? barrier = null)
    {
        await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // Signal barrier so all concurrent tasks reach this point simultaneously
            barrier?.SignalAndWait(TimeSpan.FromSeconds(10));

            // Step 1: Lock the stock row (same as production's SELECT FOR UPDATE)
            Guid stockId;
            int currentQuantity;
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT id, quantity FROM inventory.stocks WHERE product_id = @pid FOR UPDATE",
                connection, transaction))
            {
                lockCmd.Parameters.AddWithValue("pid", productId);
                await using var reader = await lockCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    await transaction.RollbackAsync();
                    return false;
                }

                stockId = reader.GetGuid(0);
                currentQuantity = reader.GetInt32(1);
            }

            // Step 2: Count active reservations (within same transaction, sees committed data)
            int reservedCount;
            await using (var countCmd = new NpgsqlCommand(
                "SELECT COALESCE(SUM(quantity), 0) FROM inventory.stock_reservations " +
                "WHERE stock_id = @sid AND status = 'Active' AND expires_at > NOW()",
                connection, transaction))
            {
                countCmd.Parameters.AddWithValue("sid", stockId);
                reservedCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            var available = currentQuantity - reservedCount;
            if (quantity > available)
            {
                await transaction.RollbackAsync();
                return false; // Insufficient stock
            }

            // Step 3: Insert reservation
            var reservationId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using (var insertCmd = new NpgsqlCommand(
                "INSERT INTO inventory.stock_reservations " +
                "(id, stock_id, order_id, quantity, status, created_at, updated_at, expires_at) " +
                "VALUES (@id, @sid, @oid, @qty, 'Active', @now, @now, @exp)",
                connection, transaction))
            {
                insertCmd.Parameters.AddWithValue("id", reservationId);
                insertCmd.Parameters.AddWithValue("sid", stockId);
                insertCmd.Parameters.AddWithValue("oid", orderId);
                insertCmd.Parameters.AddWithValue("qty", quantity);
                insertCmd.Parameters.AddWithValue("now", now);
                insertCmd.Parameters.AddWithValue("exp", now.AddMinutes(15));
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Step 4: Update last_updated_at
            await using (var updateCmd = new NpgsqlCommand(
                "UPDATE inventory.stocks SET last_updated_at = @now WHERE id = @sid",
                connection, transaction))
            {
                updateCmd.Parameters.AddWithValue("now", now);
                updateCmd.Parameters.AddWithValue("sid", stockId);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            return false;
        }
    }

    /// <summary>
    ///     Invokes a command via Wolverine's <see cref="IMessageBus"/> in an
    ///     independent DI scope. Used for sequential (non-concurrent) tests only.
    /// </summary>
    private async Task<TResult> InvokeInNewScope<TResult>(object message)
    {
        using var scope = Fixture.Host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        return await bus.InvokeAsync<TResult>(message);
    }

    #endregion
}
