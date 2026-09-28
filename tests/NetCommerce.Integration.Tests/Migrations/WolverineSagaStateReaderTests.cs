#nullable enable
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Infrastructure.Persistence;
using Shouldly;
using Wolverine.Tracking;
using Xunit;

namespace NetCommerce.Integration.Tests.Migrations;

/// <summary>
///     Verifies <see cref="WolverineSagaStateReader"/> against the real Wolverine
///     6.x saga store (<c>wolverine.orderfulfillmentsaga_saga</c>).
///
///     The reader is load-bearing for ops tooling (stuck-saga dashboard, metrics,
///     alerting, admin recovery): it must parse Wolverine's actual JSON shape
///     (state encoding, Money object, casing) and delete rows so late messages
///     land in saga NotFound handlers. A drift in Wolverine's storage format
///     must fail HERE, not silently in production dashboards.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "WolverineDrift")]
public sealed class WolverineSagaStateReaderTests : IntegrationTestBase
{
    public WolverineSagaStateReaderTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task QuerySagas_ShouldReadLiveSagaRow_WithCorrectStateAndAmount()
    {
        var productId = Guid.NewGuid();
        var sku = $"SKU-SAGAREAD-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();

        await using (var inventoryDb = Fixture.CreateInventoryDbContext())
        {
            var stock = Stock.Create(productId, sku, 50);
            inventoryDb.Stocks.Add(stock);
            await inventoryDb.SaveChangesAsync();
        }

        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-SAGAREAD-001",
            Money.Create(199.99m, "GEL"),
            [new OrderItemReservation(productId, 2, sku)]);

        // Drive the saga to InGracePeriod (waits for InventoryReserved).
        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        await using var orderingDb = Fixture.CreateOrderingDbContext();

        // Poll until the saga row is visible (store writes are async).
        IReadOnlyList<SagaStateRow>? rows = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            rows = await WolverineSagaStateReader.QuerySagasAsync(orderingDb);
            if (rows.Any(r => r.OrderId == orderId))
                break;

            await Task.Delay(200);
        }

        var row = rows!.Single(r => r.OrderId == orderId);
        row.OrderNumber.ShouldBe("ORD-SAGAREAD-001");
        row.State.ShouldBe(OrderFulfillmentState.InGracePeriod);
        row.TotalAmount.Amount.ShouldBe(199.99m);
        row.TotalAmount.Currency.ShouldBe("GEL");

        // State filtering must work (used by stuck-saga sweeps).
        var failed = await WolverineSagaStateReader.QuerySagasAsync(
            orderingDb, OrderFulfillmentState.Failed);
        failed.ShouldNotContain(r => r.OrderId == orderId);

        var inGrace = await WolverineSagaStateReader.QuerySagasAsync(
            orderingDb, OrderFulfillmentState.InGracePeriod);
        inGrace.ShouldContain(r => r.OrderId == orderId);
    }

    [Fact]
    public async Task DeleteSaga_ShouldRemoveRow_SoLateMessagesHitNotFound()
    {
        var productId = Guid.NewGuid();
        var sku = $"SKU-SAGADEL-{Guid.NewGuid():N}";
        var orderId = Guid.NewGuid();

        await using (var inventoryDb = Fixture.CreateInventoryDbContext())
        {
            var stock = Stock.Create(productId, sku, 50);
            inventoryDb.Stocks.Add(stock);
            await inventoryDb.SaveChangesAsync();
        }

        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-SAGADEL-001",
            Money.Create(49.99m, "GEL"),
            [new OrderItemReservation(productId, 1, sku)]);

        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        await using var orderingDb = Fixture.CreateOrderingDbContext();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var visible = await WolverineSagaStateReader.QuerySagasAsync(orderingDb);
            if (visible.Any(r => r.OrderId == orderId))
                break;

            await Task.Delay(200);
        }

        var deleted = await WolverineSagaStateReader.DeleteSagaAsync(orderingDb, orderId);
        deleted.ShouldBe(1);

        var after = await WolverineSagaStateReader.QuerySagasAsync(orderingDb);
        after.ShouldNotContain(r => r.OrderId == orderId);

        // Second delete is a no-op (idempotent admin retries).
        var deletedAgain = await WolverineSagaStateReader.DeleteSagaAsync(orderingDb, orderId);
        deletedAgain.ShouldBe(0);
    }
}
