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
///     End-to-end integration tests for the OrderFulfillmentSaga happy path.
///
///     <para>
///     Tests the complete flow: basket → order submitted → inventory reserved →
///     grace period → inventory locked → payment initiated → (webhook) payment
///     succeeded → inventory confirmed → order finalized.
///     </para>
///
///     <para>
///     <b>Key Insight:</b> The saga uses the Webhook-First pattern, so after
///     <c>PaymentInitiated</c> the saga waits for <c>PaymentSucceeded</c> from the
///     webhook bridge. In tests we simulate by manually sending the event.
///     </para>
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "RequiresDocker")]
[Trait("Category", "E2E")]
public class OrderFulfillmentSagaE2ETests : IntegrationTestBase
{
    public OrderFulfillmentSagaE2ETests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     Full happy path: Start → ReserveInventory → GracePeriod → LockInventory →
    ///     RequestPayment → PaymentInitiated → (webhook) PaymentSucceeded →
    ///     ConfirmInventory → FinalizeOrder.
    ///
    ///     <para>
    ///     Validates the complete order fulfillment lifecycle with real database operations.
    ///     Each saga step is verified through Wolverine tracked sessions and database state.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task FullHappyPath_BasketToConfirmed_ShouldCompleteAllSteps()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Seed stock and prepare order
        // ═══════════════════════════════════════════════════════════════════════
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var sku1 = $"SKU-E2E-A-{Guid.NewGuid():N}";
        var sku2 = $"SKU-E2E-B-{Guid.NewGuid():N}";

        await CreateTestStockAsync(productId1, sku1, 50);
        await CreateTestStockAsync(productId2, sku2, 30);

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = Money.Create(349.98m, "GEL");

        var items = new List<OrderItemReservation>
        {
            new(productId1, 2, sku1),
            new(productId2, 1, sku2)
        };

        // ═══════════════════════════════════════════════════════════════════════
        // STEP 1: Start saga → Inventory Reserved
        // ═══════════════════════════════════════════════════════════════════════
        var startCommand = new StartOrderFulfillmentCommand(
            orderId, customerId, "ORD-E2E-001", amount, items);

        var step1 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryReserved>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        step1.AllExceptions().ShouldBeEmpty("Step 1 should not throw");
        step1.Sent.MessagesOf<ReserveInventoryCommand>().ShouldNotBeEmpty();
        step1.Sent.MessagesOf<InventoryReservationTimeoutMessage>().ShouldNotBeEmpty();

        // Verify inventory was reserved in DB
        var reservedItems = step1.Received.MessagesOf<InventoryReserved>().First().ReservedItems;
        reservedItems.Count.ShouldBe(2, "Both products should have reservations");

        await VerifyStockReserved(productId1, expectedReserved: 2);
        await VerifyStockReserved(productId2, expectedReserved: 1);

        // ═══════════════════════════════════════════════════════════════════════
        // STEP 2: Grace period fires → Inventory Locked → Payment Initiated
        // ═══════════════════════════════════════════════════════════════════════
        // The GracePeriodTimeout is a scheduled message. We fire it manually.
        // This cascades: GracePeriodTimeout → LockInventoryForPaymentCommand
        // → InventoryLocked → RequestPaymentCommand → PaymentInitiated
        var gracePeriodTimeout = new GracePeriodTimeout { Id = orderId };

        var step2 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<PaymentInitiated>(Fixture.Host)
            .InvokeMessageAndWaitAsync(gracePeriodTimeout);

        step2.AllExceptions().ShouldBeEmpty("Step 2 should not throw");
        step2.Sent.MessagesOf<LockInventoryForPaymentCommand>().ShouldNotBeEmpty();
        step2.Sent.MessagesOf<RequestPaymentCommand>().ShouldNotBeEmpty();

        // Verify a PaymentTransaction was created in the database
        await using var paymentsCtx = Fixture.CreatePaymentsDbContext();
        var paymentTxn = await paymentsCtx.Transactions
            .FirstOrDefaultAsync(t => t.OrderId == orderId);
        paymentTxn.ShouldNotBeNull("Payment transaction should exist");
        paymentTxn.ExternalTransactionId.ShouldNotBeNullOrEmpty();

        // ═══════════════════════════════════════════════════════════════════════
        // STEP 3: Simulate webhook → PaymentSucceeded → ConfirmInventory
        // ═══════════════════════════════════════════════════════════════════════
        // In production, Stripe sends webhook → ProcessExternalPaymentConfirmation
        // → PaymentCompletedDomainEvent → PaymentSucceeded.
        // We simulate by sending PaymentSucceeded directly to the saga.
        var paymentSucceeded = new PaymentSucceeded(
            orderId,
            paymentTxn.ExternalTransactionId!,
            amount);

        var step3 = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(15))
            .WaitForMessageToBeReceivedAt<InventoryConfirmed>(Fixture.Host)
            .InvokeMessageAndWaitAsync(paymentSucceeded);

        step3.AllExceptions().ShouldBeEmpty("Step 3 should not throw");
        step3.Sent.MessagesOf<ConfirmInventoryCommand>().ShouldNotBeEmpty();

        // ═══════════════════════════════════════════════════════════════════════
        // STEP 4: Verify final state — inventory deducted, saga completed
        // ═══════════════════════════════════════════════════════════════════════
        step3.Sent.MessagesOf<FinalizeOrderCommand>().ShouldNotBeEmpty(
            "FinalizeOrderCommand should be sent on successful inventory confirmation");

        // Verify inventory was deducted (confirmed = quantity permanently reduced)
        await using var invCtx = Fixture.CreateInventoryDbContext();
        var stock1 = await invCtx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId1);
        var stock2 = await invCtx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId2);

        // After confirmation, Quantity is reduced and reservation is Confirmed
        stock1.Quantity.ShouldBe(48, "Stock1 should have 50 - 2 = 48 after confirmation");
        stock2.Quantity.ShouldBe(29, "Stock2 should have 30 - 1 = 29 after confirmation");

        var confirmedReservations1 = stock1.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed).ToList();
        confirmedReservations1.ShouldNotBeEmpty("Reservation should be marked as Confirmed");
    }

    /// <summary>
    ///     Verifies that inventory reservation for multiple items is atomic:
    ///     if one item is out of stock, none are reserved.
    /// </summary>
    [Fact]
    public async Task AtomicReservation_OneItemOutOfStock_ShouldFailEntireOrder()
    {
        // Arrange — product2 has 0 stock
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var sku1 = $"SKU-ATOMIC-A-{Guid.NewGuid():N}";
        var sku2 = $"SKU-ATOMIC-B-{Guid.NewGuid():N}";

        await CreateTestStockAsync(productId1, sku1, 100);
        await CreateTestStockAsync(productId2, sku2, 0); // Out of stock!

        var orderId = Guid.NewGuid();
        var items = new List<OrderItemReservation>
        {
            new(productId1, 1, sku1),
            new(productId2, 1, sku2) // Will fail — insufficient stock
        };

        var startCommand = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-ATOMIC-001", Money.Create(100m), items);

        // Act
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .WaitForMessageToBeReceivedAt<FailOrderCommand>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        // Assert — entire order should fail, product1 should NOT be reserved
        tracked.AllExceptions().ShouldBeEmpty();
        tracked.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty();

        await using var invCtx = Fixture.CreateInventoryDbContext();
        var stock1 = await invCtx.Stocks
            .Include(s => s.Reservations)
            .FirstAsync(s => s.ProductId == productId1);

        // Transaction rolled back — no active reservation should exist for product1
        stock1.Reservations.Where(r => r.Status == ReservationStatus.Active)
            .ShouldBeEmpty("Atomic reservation: partial reservation should not persist");
    }

    /// <summary>
    ///     Verifies the saga correctly handles the GracePeriodTimeout arriving after
    ///     the saga has already failed at inventory reservation.
    /// </summary>
    [Fact]
    public async Task GracePeriodTimeout_AfterSagaFailed_ShouldBeIgnored()
    {
        // Arrange — No stock, saga will fail at inventory step
        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-LATE-GP",
            Money.Create(50m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-NONE")]);

        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(startCommand);

        // Act — Send GracePeriodTimeout to completed/failed saga
        var timeout = new GracePeriodTimeout { Id = orderId };
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(timeout);

        // Assert — Should not produce any lock commands (saga is done)
        tracked.Sent.MessagesOf<LockInventoryForPaymentCommand>().ShouldBeEmpty(
            "GracePeriodTimeout after saga failure should not cascade lock command");
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
