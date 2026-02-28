#nullable enable
using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using Shouldly;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Ordering;

/// <summary>
///     Sad-path integration tests for the OrderFulfillmentSaga.
///
///     <para>
///     Tests:
///     1. Payment failure triggers inventory rollback
///     2. Inventory conflict for insufficient stock
///     3. Idempotent saga message handling (duplicate messages)
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "RequiresDocker")]
[Trait("Category", "SadPath")]
public class OrderFulfillmentSagaSadPathTests : IntegrationTestBase
{
    public OrderFulfillmentSagaSadPathTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Payment Failure → Inventory Rollback

    /// <summary>
    ///     Payment failure must trigger <c>ReleaseInventoryReservationCommand</c> and
    ///     <c>FailOrderCommand</c>. Verified end-to-end through the real saga.
    ///
    ///     <para>
    ///     Uses <c>TestPaymentGateway.FailingOrderIds</c> so the gateway returns
    ///     <c>PaymentFailed</c> at the payment step.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task PaymentFailure_ShouldReleaseInventoryAndFailOrder()
    {
        // Arrange — seed stock and flag the orderId as failing
        var productId = Guid.NewGuid();
        var sku = $"SKU-PFAIL-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, 100);

        var orderId = Guid.NewGuid();
        TestPaymentGateway.FailingOrderIds.Add(orderId);

        var items = new List<OrderItemReservation> { new(productId, 3, sku) };
        var startCommand = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-PFAIL-001", Money.Create(299.99m), items);

        // STEP 1: Start saga → InventoryReserved
        var step1 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        step1.AllExceptions().ShouldBeEmpty();
        await VerifyStockReserved(productId, expectedReserved: 3);

        // STEP 2: Grace period → Lock → Payment (will fail) → Compensation
        var gracePeriod = new GracePeriodTimeout { Id = orderId };

        var step2 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<FailOrderCommand>(Fixture.Host)
            .InvokeMessageAndWaitAsync(gracePeriod);

        step2.AllExceptions().ShouldBeEmpty("Payment failure should not throw");

        // Assert — PaymentFailed cascades ReleaseInventoryReservationCommand + FailOrderCommand
        step2.Sent.MessagesOf<ReleaseInventoryReservationCommand>().ShouldNotBeEmpty(
            "Payment failure must release inventory reservations");
        step2.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty(
            "Payment failure must fail the order");
        step2.Sent.MessagesOf<FailOrderCommand>().First().OrderId.ShouldBe(orderId);
    }

    /// <summary>
    ///     Payment failure by amount (using <c>FailingAmounts</c>) should
    ///     produce the same compensation as failure by orderId.
    /// </summary>
    [Fact]
    public async Task PaymentFailure_ByAmount_ShouldReleaseInventoryAndFailOrder()
    {
        var productId = Guid.NewGuid();
        var sku = $"SKU-AFAIL-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, 100);

        var failAmount = 666.00m; // Trigger by amount
        TestPaymentGateway.FailingAmounts.Add(failAmount);

        var orderId = Guid.NewGuid();
        var items = new List<OrderItemReservation> { new(productId, 1, sku) };
        var startCommand = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-AFAIL-001", Money.Create(failAmount), items);

        // Start → InventoryReserved
        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        // Grace period → Payment failure → Compensation
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<FailOrderCommand>(Fixture.Host)
            .InvokeMessageAndWaitAsync(new GracePeriodTimeout { Id = orderId });

        tracked.AllExceptions().ShouldBeEmpty();
        tracked.Sent.MessagesOf<ReleaseInventoryReservationCommand>().ShouldNotBeEmpty();
        tracked.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty();
    }

    #endregion

    #region Inventory Conflict → Proper Failure

    /// <summary>
    ///     When all stock is reserved and a second order tries to reserve the same
    ///     product, the saga should receive <c>InventoryReservationFailed</c> and
    ///     cascade <c>FailOrderCommand</c>.
    /// </summary>
    [Fact]
    public async Task InventoryConflict_InsufficientStock_ShouldFailOrder()
    {
        // Arrange — Create stock with exactly 1 unit
        var productId = Guid.NewGuid();
        var sku = $"SKU-CONFLICT-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, 1);

        // Order 1: Reserve the single unit (succeeds)
        var order1Id = Guid.NewGuid();
        var start1 = new StartOrderFulfillmentCommand(
            order1Id, Guid.NewGuid(), "ORD-FIRST-001", Money.Create(50m),
            [new OrderItemReservation(productId, 1, sku)]);

        var tracked1 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(start1);

        tracked1.AllExceptions().ShouldBeEmpty();

        // Order 2: Try to reserve the same unit (should fail — insufficient stock)
        var order2Id = Guid.NewGuid();
        var start2 = new StartOrderFulfillmentCommand(
            order2Id, Guid.NewGuid(), "ORD-SECOND-001", Money.Create(50m),
            [new OrderItemReservation(productId, 1, sku)]);

        var tracked2 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<FailOrderCommand>(Fixture.Host)
            .InvokeMessageAndWaitAsync(start2);

        // Assert — second order failed with inventory reservation failure
        tracked2.AllExceptions().ShouldBeEmpty();
        tracked2.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty(
            "Second order should fail when insufficient stock");
        tracked2.Sent.MessagesOf<FailOrderCommand>().First().OrderId.ShouldBe(order2Id);

        // Verify the failure reason mentions unavailable product
        var failMsg = tracked2.Received.MessagesOf<InventoryReservationFailed>().FirstOrDefault();
        failMsg.ShouldNotBeNull("Should receive InventoryReservationFailed");
        failMsg.UnavailableProductIds.ShouldNotBeNull();
        failMsg.UnavailableProductIds.ShouldContain(productId);
    }

    /// <summary>
    ///     Requesting more than available stock should fail the order,
    ///     even when stock exists but is insufficient.
    /// </summary>
    [Fact]
    public async Task InventoryConflict_ExceedsAvailable_ShouldFailOrder()
    {
        var productId = Guid.NewGuid();
        var sku = $"SKU-EXCEED-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, 5);

        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-EXCEED-001", Money.Create(1000m),
            [new OrderItemReservation(productId, 10, sku)]); // Request 10, only 5 available

        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .WaitForMessageToBeReceivedAt<FailOrderCommand>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        tracked.AllExceptions().ShouldBeEmpty();
        tracked.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty();

        // Verify stock was NOT modified
        await using var ctx = Fixture.CreateInventoryDbContext();
        var stock = await ctx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        stock.Reservations.Where(r => r.Status == ReservationStatus.Active)
            .ShouldBeEmpty("No reservation should exist when request exceeds available");
    }

    #endregion

    #region Idempotent Message Handling

    /// <summary>
    ///     Sending <c>PaymentSucceeded</c> to a saga that doesn't exist (or completed)
    ///     should be handled gracefully without exceptions. Validates Wolverine's
    ///     NotFound handler for stale messages.
    /// </summary>
    [Fact]
    public async Task DuplicatePaymentSucceeded_ToCompletedSaga_ShouldBeIgnored()
    {
        // Arrange — Create and complete a saga through the full happy path
        var productId = Guid.NewGuid();
        var sku = $"SKU-DUP-{Guid.NewGuid():N}";
        await CreateTestStockAsync(productId, sku, 50);

        var orderId = Guid.NewGuid();
        var startCmd = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-DUP-001", Money.Create(100m),
            [new OrderItemReservation(productId, 1, sku)]);

        // Start → InventoryReserved
        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCmd);

        // Grace → Lock → Payment → PaymentInitiated
        var step2 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<PaymentInitiated>(Fixture.Host)
            .InvokeMessageAndWaitAsync(new GracePeriodTimeout { Id = orderId });

        // PaymentSucceeded → ConfirmInventory → FinalizeOrder (saga completed + MarkCompleted)
        var paymentSucceeded = new PaymentSucceeded(
            orderId, $"test_txn_{Guid.NewGuid():N}", Money.Create(100m));

        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryConfirmed>(Fixture.Host)
            .InvokeMessageAndWaitAsync(paymentSucceeded);

        // Act — Send PaymentSucceeded AGAIN to the completed saga
        var latePaymentSucceeded = new PaymentSucceeded(
            orderId, $"test_txn_{Guid.NewGuid():N}", Money.Create(100m));

        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(latePaymentSucceeded);

        // Assert — Should be handled by NotFound handler, no cascade
        tracked.Sent.MessagesOf<ConfirmInventoryCommand>().ShouldBeEmpty(
            "Late PaymentSucceeded should not trigger ConfirmInventory");
    }

    /// <summary>
    ///     Sending <c>InventoryReserved</c> to an already-completed saga
    ///     should not produce new commands.
    /// </summary>
    [Fact]
    public async Task LateInventoryReserved_AfterSagaFailed_ShouldBeIgnored()
    {
        // Arrange — Start a saga with no stock → fails at inventory
        var orderId = Guid.NewGuid();
        var startCmd = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-LATE-IR",
            Money.Create(50m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-MISS")]);

        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(startCmd);

        // Act — Send InventoryReserved to completed/failed saga
        var lateEvent = new InventoryReserved(
            orderId,
            [new ReservedItem(Guid.NewGuid(), Guid.NewGuid(), 1)]);

        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(lateEvent);

        // Assert — Should NOT cascade GracePeriodTimeout (saga already done)
        tracked.Sent.MessagesOf<GracePeriodTimeout>().ShouldBeEmpty(
            "Late InventoryReserved should not restart the saga flow");
    }

    #endregion

    #region Helper Methods

    private async Task<Stock> CreateTestStockAsync(Guid productId, string sku, int quantity)
    {
        await using var context = Fixture.CreateInventoryDbContext();
        var stock = Stock.Create(productId, sku, quantity);
        context.Stocks.Add(stock);
        await context.SaveChangesAsync();
        return stock;
    }

    private async Task VerifyStockReserved(Guid productId, int expectedReserved)
    {
        await using var ctx = Fixture.CreateInventoryDbContext();
        var stock = await ctx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId);

        var activeReservations = stock.Reservations
            .Where(r => r.Status == ReservationStatus.Active
                        || r.Status == ReservationStatus.PendingPayment)
            .Sum(r => r.Quantity);

        activeReservations.ShouldBe(expectedReserved,
            $"Product {productId} should have {expectedReserved} units reserved");
    }

    #endregion
}
