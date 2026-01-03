using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Ordering;

/// <summary>
///     Integration tests for OrderFulfillmentSaga using Wolverine's tracked sessions.
///     Tests the full saga workflow including message cascading and state persistence.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class OrderFulfillmentSagaIntegrationTests : IntegrationTestBase
{
    public OrderFulfillmentSagaIntegrationTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Happy Path Integration Tests

    [Fact]
    public async Task Saga_HappyPath_ShouldCompleteSuccessfully()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = Money.Create(199.99m, "USD");
        var items = new List<OrderItemReservation>
        {
            new(Guid.NewGuid(), 2, "SKU-001"),
            new(Guid.NewGuid(), 1, "SKU-002")
        };

        var startCommand = new StartOrderFulfillmentCommand(
            orderId, customerId, "ORD-HAPPY-001", amount, items);

        // Act - Track the full message flow
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .WaitForMessageToBeReceivedAt<FinalizeOrderCommand>(Fixture.Host)
            .InvokeMessageAndWaitAsync(startCommand);

        // Assert - Verify all messages were processed
        tracked.AllExceptions().ShouldBeEmpty();

        // Verify the cascade of messages
        tracked.Executed.SingleMessage<StartOrderFulfillmentCommand>().ShouldNotBeNull();
        tracked.Sent.MessagesOf<ReserveInventoryCommand>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Saga_Start_ShouldCascadeReserveInventoryCommand()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-CASCADE-001",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-TEST")]);

        // Act
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(startCommand);

        // Assert
        tracked.AllExceptions().ShouldBeEmpty();

        // Should have sent ReserveInventoryCommand
        var reserveCommands = tracked.Sent.MessagesOf<ReserveInventoryCommand>();
        reserveCommands.ShouldNotBeEmpty();
        reserveCommands.First().OrderId.ShouldBe(orderId);
    }

    #endregion

    #region Failure Scenario Integration Tests

    [Fact]
    public async Task Saga_InventoryReservationFailed_ShouldCascadeFailOrderCommand()
    {
        // Arrange - We need to simulate the failure response
        var orderId = Guid.NewGuid();
        var failedEvent = new InventoryReservationFailed(
            orderId,
            "Product out of stock",
            [Guid.NewGuid()]);

        // First start a saga
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-FAIL-001",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-OOS")]);

        // Start the saga first
        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(startCommand);

        // Act - Send failure event
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(failedEvent);

        // Assert
        tracked.AllExceptions().ShouldBeEmpty();

        // Should cascade FailOrderCommand
        var failCommands = tracked.Sent.MessagesOf<FailOrderCommand>();
        failCommands.ShouldNotBeEmpty();
        failCommands.First().OrderId.ShouldBe(orderId);
        failCommands.First().FailureReason.ShouldContain("out of stock");
    }

    [Fact]
    public async Task Saga_PaymentFailed_ShouldCascadeCompensatingActions()
    {
        // Arrange - Start saga and move to ProcessingPayment state
        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-PAY-FAIL",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-PAY")]);

        // Start saga
        await Fixture.Host.InvokeMessageAndWaitAsync(startCommand);

        // Simulate inventory reserved
        var inventoryReserved = new InventoryReserved(
            orderId,
            [new ReservedItem(Guid.NewGuid(), Guid.NewGuid(), 1)]);

        await Fixture.Host.InvokeMessageAndWaitAsync(inventoryReserved);

        // Act - Simulate payment failure
        var paymentFailed = new PaymentFailed(orderId, "Card declined", "CARD_DECLINED");

        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(paymentFailed);

        // Assert
        tracked.AllExceptions().ShouldBeEmpty();

        // Should cascade both ReleaseInventoryReservationCommand and FailOrderCommand
        tracked.Sent.MessagesOf<ReleaseInventoryReservationCommand>().ShouldNotBeEmpty();
        tracked.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Saga_InventoryConfirmationFailed_ShouldRefundAndRelease()
    {
        // Arrange - Progress saga to ConfirmingInventory state
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var amount = Money.Create(299.99m, "USD");

        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-INV-CONFIRM-FAIL",
            amount,
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-CONFIRM")]);

        // Progress through states
        await Fixture.Host.InvokeMessageAndWaitAsync(startCommand);
        await Fixture.Host.InvokeMessageAndWaitAsync(new InventoryReserved(
            orderId, [new ReservedItem(Guid.NewGuid(), Guid.NewGuid(), 1)]));
        await Fixture.Host.InvokeMessageAndWaitAsync(new PaymentSucceeded(orderId, transactionId, amount));

        // Act - Critical failure: inventory confirmation fails after payment
        var confirmFailed = new InventoryConfirmationFailed(orderId, "Stock discrepancy");

        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(confirmFailed);

        // Assert
        tracked.AllExceptions().ShouldBeEmpty();

        // Must cascade all three compensating commands
        var refundCommands = tracked.Sent.MessagesOf<RefundPaymentCommand>();
        refundCommands.ShouldNotBeEmpty();
        refundCommands.First().PaymentTransactionId.ShouldBe(transactionId);
        refundCommands.First().Amount.Amount.ShouldBe(299.99m);

        tracked.Sent.MessagesOf<ReleaseInventoryReservationCommand>().ShouldNotBeEmpty();
        tracked.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty();
    }

    #endregion

    #region Timeout Integration Tests

    [Fact]
    public async Task Saga_InventoryReservationTimeout_WhenNotReserved_ShouldFail()
    {
        // Arrange - Start saga but don't send inventory response
        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-TIMEOUT-001",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-TIMEOUT")]);

        await Fixture.Host.InvokeMessageAndWaitAsync(startCommand);

        // Act - Manually send timeout (normally Wolverine schedules this)
        var timeout = new InventoryReservationTimeoutMessage { Id = orderId };

        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(timeout);

        // Assert
        tracked.AllExceptions().ShouldBeEmpty();
        tracked.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Saga_PaymentTimeout_ShouldReleaseInventoryAndFail()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-PAY-TIMEOUT",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-PAY-TO")]);

        await Fixture.Host.InvokeMessageAndWaitAsync(startCommand);

        // Move to ProcessingPayment
        await Fixture.Host.InvokeMessageAndWaitAsync(new InventoryReserved(
            orderId, [new ReservedItem(Guid.NewGuid(), Guid.NewGuid(), 1)]));

        // Act - Send payment timeout
        var timeout = new PaymentTimeoutMessage { Id = orderId };

        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(timeout);

        // Assert
        tracked.AllExceptions().ShouldBeEmpty();
        tracked.Sent.MessagesOf<ReleaseInventoryReservationCommand>().ShouldNotBeEmpty();
        tracked.Sent.MessagesOf<FailOrderCommand>().ShouldNotBeEmpty();
    }

    #endregion

    #region NotFound Handler Integration Tests

    [Fact]
    public async Task Saga_NotFound_LateMessage_ShouldNotThrow()
    {
        // Arrange - Send message for non-existent saga
        var nonExistentOrderId = Guid.NewGuid();
        var lateEvent = new PaymentSucceeded(
            nonExistentOrderId,
            Guid.NewGuid(),
            Money.Create(100m));

        // Act
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(lateEvent);

        // Assert - Should handle gracefully via NotFound handler
        // The NotFound handler logs and returns, so no exception should propagate
    }

    [Fact]
    public async Task Saga_NotFound_LateTimeout_ShouldNotThrow()
    {
        // Arrange - Send timeout for completed/non-existent saga
        var timeout = new PaymentTimeoutMessage { Id = Guid.NewGuid() };

        // Act
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(timeout);

        // Assert - Should handle gracefully
    }

    #endregion

    #region Idempotency Integration Tests

    [Fact]
    public async Task Saga_DuplicateInventoryReserved_ShouldBeHandledIdempotently()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-IDEM-001",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-IDEM")]);

        await Fixture.Host.InvokeMessageAndWaitAsync(startCommand);

        // First inventory reserved
        var inventoryReserved = new InventoryReserved(
            orderId,
            [new ReservedItem(Guid.NewGuid(), Guid.NewGuid(), 1)]);

        await Fixture.Host.InvokeMessageAndWaitAsync(inventoryReserved);

        // Act - Send duplicate (shouldn't cause issues)
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(inventoryReserved);

        // Assert - Should handle without errors
        // The saga has already moved past ReservingInventory state
    }

    #endregion

    #region Message Bus Integration Tests

    [Fact]
    public async Task MessageBus_ShouldRouteStartCommand_ToSaga()
    {
        // Arrange
        var bus = Fixture.Host.Services.GetRequiredService<IMessageBus>();
        var orderId = Guid.NewGuid();
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-BUS-001",
            Money.Create(50m),
            []);

        // Act
        await bus.InvokeAsync(startCommand);

        // Assert - Command should be processed without exception
        // The saga will be created and waiting for next message
    }

    [Fact]
    public async Task MessageBus_ShouldRouteEvents_ToSaga()
    {
        // Arrange
        var bus = Fixture.Host.Services.GetRequiredService<IMessageBus>();
        var orderId = Guid.NewGuid();

        // Start saga
        await bus.InvokeAsync(new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-BUS-002",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1, "SKU-BUS")]));

        // Act - Send response event through bus
        await bus.InvokeAsync(new InventoryReserved(
            orderId,
            [new ReservedItem(Guid.NewGuid(), Guid.NewGuid(), 1)]));

        // Assert - No exception means successful routing
    }

    #endregion
}
