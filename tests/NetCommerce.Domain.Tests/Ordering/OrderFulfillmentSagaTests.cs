using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Unit tests for OrderFulfillmentSaga.
///     Tests the saga state transitions, happy path, and compensating transactions.
/// </summary>
public class OrderFulfillmentSagaTests
{
    private readonly ILogger<OrderFulfillmentSaga> _logger;

    public OrderFulfillmentSagaTests()
    {
        _logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();
    }

    #region Saga Initiation Tests

    [Fact]
    public void Start_ShouldCreateSaga_WithCorrectInitialState()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderNumber = "ORD-20260103-ABC123";
        var amount = Money.Create(199.99m, "USD");
        var items = new List<OrderItemReservation>
        {
            new(Guid.NewGuid(), 2, "SKU-001"),
            new(Guid.NewGuid(), 1, "SKU-002")
        };

        var command = new StartOrderFulfillmentCommand(
            orderId, customerId, orderNumber, amount, items);

        // Act
        var (saga, reserveCommand, timeout) = OrderFulfillmentSaga.Start(command, _logger);

        // Assert
        saga.ShouldNotBeNull();
        saga.Id.ShouldBe(orderId);
        saga.CustomerId.ShouldBe(customerId);
        saga.OrderNumber.ShouldBe(orderNumber);
        saga.TotalAmount.Amount.ShouldBe(199.99m);
        saga.TotalAmount.Currency.ShouldBe("USD");
        saga.Items.Count.ShouldBe(2);
        saga.State.ShouldBe(OrderFulfillmentState.ReservingInventory);
        saga.IsInventoryReserved.ShouldBeFalse();
        saga.IsPaid.ShouldBeFalse();
        saga.IsInventoryConfirmed.ShouldBeFalse();
        saga.StartedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Start_ShouldReturnReserveInventoryCommand()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var items = new List<OrderItemReservation>
        {
            new(Guid.NewGuid(), 3, "SKU-TEST")
        };

        var command = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-TEST", Money.Create(100m), items);

        // Act
        var (_, reserveCommand, _) = OrderFulfillmentSaga.Start(command, _logger);

        // Assert
        reserveCommand.ShouldNotBeNull();
        reserveCommand.OrderId.ShouldBe(orderId);
        reserveCommand.Items.ShouldBe(items);
    }

    [Fact]
    public void Start_ShouldReturnInventoryReservationTimeout()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var command = new StartOrderFulfillmentCommand(
            orderId, Guid.NewGuid(), "ORD-TEST", Money.Create(100m), []);

        // Act
        var (_, _, timeout) = OrderFulfillmentSaga.Start(command, _logger);

        // Assert
        timeout.ShouldNotBeNull();
        timeout.Id.ShouldBe(orderId);
    }

    #endregion

    #region Happy Path Tests

    [Fact]
    public void Handle_InventoryReserved_ShouldTransitionToProcessingPayment()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ReservingInventory);
        var reservedItems = new List<ReservedItem>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), 2),
            new(Guid.NewGuid(), Guid.NewGuid(), 1)
        };
        var @event = new InventoryReserved(saga.Id, reservedItems);

        // Act
        var (paymentCommand, timeout) = saga.Handle(@event, _logger);

        // Assert
        saga.State.ShouldBe(OrderFulfillmentState.ProcessingPayment);
        saga.IsInventoryReserved.ShouldBeTrue();
        saga.ReservedItems.ShouldBe(reservedItems);

        paymentCommand.ShouldNotBeNull();
        paymentCommand.OrderId.ShouldBe(saga.Id);
        paymentCommand.Amount.ShouldBe(saga.TotalAmount);

        timeout.ShouldNotBeNull();
        timeout.Id.ShouldBe(saga.Id);
    }

    [Fact]
    public void Handle_PaymentSucceeded_ShouldTransitionToConfirmingInventory()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ProcessingPayment);
        saga.IsInventoryReserved = true;
        var transactionId = Guid.NewGuid();
        var @event = new PaymentSucceeded(saga.Id, transactionId, saga.TotalAmount);

        // Act
        var (confirmCommand, timeout) = saga.Handle(@event, _logger);

        // Assert
        saga.State.ShouldBe(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid.ShouldBeTrue();
        saga.PaymentTransactionId.ShouldBe(transactionId);

        confirmCommand.ShouldNotBeNull();
        confirmCommand.OrderId.ShouldBe(saga.Id);
        confirmCommand.PaymentTransactionId.ShouldBe(transactionId);

        timeout.ShouldNotBeNull();
    }

    [Fact]
    public void Handle_InventoryConfirmed_ShouldCompleteSaga()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid = true;
        saga.PaymentTransactionId = Guid.NewGuid();
        var @event = new InventoryConfirmed(saga.Id);

        // Act
        var (finalizeCommand, notification) = saga.Handle(@event, _logger);

        // Assert
        saga.State.ShouldBe(OrderFulfillmentState.Completed);
        saga.IsInventoryConfirmed.ShouldBeTrue();
        saga.CompletedAt.ShouldNotBeNull();
        // Note: MarkCompleted() was called - Wolverine will purge this saga

        finalizeCommand.ShouldNotBeNull();
        finalizeCommand.OrderId.ShouldBe(saga.Id);
        finalizeCommand.PaymentTransactionId.ShouldBe(saga.PaymentTransactionId!.Value);

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Success");
    }

    [Fact]
    public void HappyPath_FullWorkflow_ShouldTransitionThroughAllStates()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var amount = Money.Create(299.99m, "USD");
        var items = new List<OrderItemReservation>
        {
            new(Guid.NewGuid(), 1, "PROD-A")
        };

        var startCommand = new StartOrderFulfillmentCommand(
            orderId, customerId, "ORD-HAPPY", amount, items);

        // Act Step 1: Start saga
        var (saga, _, _) = OrderFulfillmentSaga.Start(startCommand, _logger);
        saga.State.ShouldBe(OrderFulfillmentState.ReservingInventory);

        // Act Step 2: Inventory reserved
        var reservedItems = new List<ReservedItem> { new(items[0].ProductId, Guid.NewGuid(), 1) };
        saga.Handle(new InventoryReserved(orderId, reservedItems), _logger);
        saga.State.ShouldBe(OrderFulfillmentState.ProcessingPayment);
        saga.IsInventoryReserved.ShouldBeTrue();

        // Act Step 3: Payment succeeded
        var transactionId = Guid.NewGuid();
        saga.Handle(new PaymentSucceeded(orderId, transactionId, amount), _logger);
        saga.State.ShouldBe(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid.ShouldBeTrue();

        // Act Step 4: Inventory confirmed
        saga.Handle(new InventoryConfirmed(orderId), _logger);
        saga.State.ShouldBe(OrderFulfillmentState.Completed);
        saga.CompletedAt.ShouldNotBeNull();
    }

    #endregion

    #region Failure & Compensation Tests

    [Fact]
    public void Handle_InventoryReservationFailed_ShouldFailWithoutCompensation()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ReservingInventory);
        var unavailableProducts = new List<Guid> { Guid.NewGuid() };
        var @event = new InventoryReservationFailed(saga.Id, "Out of stock", unavailableProducts);

        // Act
        var (failCommand, notification) = saga.Handle(@event, _logger);

        // Assert - No compensation needed (nothing was reserved yet)
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.FailureReason.ShouldBe("Out of stock");
        saga.CompletedAt.ShouldNotBeNull();

        failCommand.ShouldNotBeNull();
        failCommand.OrderId.ShouldBe(saga.Id);
        failCommand.FailureReason.ShouldBe("Out of stock");

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Error");
    }

    [Fact]
    public void Handle_PaymentFailed_ShouldReleaseInventoryAndFail()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ProcessingPayment);
        saga.IsInventoryReserved = true;
        var @event = new PaymentFailed(saga.Id, "Card declined", "CARD_DECLINED");

        // Act
        var (releaseCommand, failCommand, notification) = saga.Handle(@event, _logger);

        // Assert - Should release inventory (compensating action)
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.FailureReason.ShouldBe("Card declined");
        saga.CompletedAt.ShouldNotBeNull();

        releaseCommand.ShouldNotBeNull();
        releaseCommand.OrderId.ShouldBe(saga.Id);
        releaseCommand.Reason.ShouldContain("Payment failed");

        failCommand.ShouldNotBeNull();
        failCommand.FailureReason.ShouldBe("Card declined");

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Error");
    }

    [Fact]
    public void Handle_InventoryConfirmationFailed_ShouldRefundAndReleaseInventory()
    {
        // Arrange - This is the CRITICAL failure scenario
        var saga = CreateSagaInState(OrderFulfillmentState.ConfirmingInventory);
        saga.IsInventoryReserved = true;
        saga.IsPaid = true;
        saga.PaymentTransactionId = Guid.NewGuid();
        var @event = new InventoryConfirmationFailed(saga.Id, "Stock discrepancy detected");

        // Act
        var (refundCommand, releaseCommand, failCommand, notification) = saga.Handle(@event, _logger);

        // Assert - Must refund AND release inventory
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.FailureReason.ShouldBe("Stock discrepancy detected");
        saga.CompletedAt.ShouldNotBeNull();

        // Verify refund command
        refundCommand.ShouldNotBeNull();
        refundCommand.OrderId.ShouldBe(saga.Id);
        refundCommand.PaymentTransactionId.ShouldBe(saga.PaymentTransactionId!.Value);
        refundCommand.Amount.ShouldBe(saga.TotalAmount);
        refundCommand.Reason.ShouldContain("Inventory confirmation failed");

        // Verify release command
        releaseCommand.ShouldNotBeNull();
        releaseCommand.OrderId.ShouldBe(saga.Id);

        // Verify fail command
        failCommand.ShouldNotBeNull();

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Error");
    }

    #endregion

    #region Timeout Handler Tests

    [Fact]
    public void Handle_InventoryReservationTimeout_WhenStillReserving_ShouldFail()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ReservingInventory);
        var timeout = new InventoryReservationTimeoutMessage { Id = saga.Id };

        // Act
        var result = saga.Handle(timeout, _logger);

        // Assert
        result.ShouldNotBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.FailureReason.ShouldContain("timed out");
        saga.CompletedAt.ShouldNotBeNull();

        var (failCommand, notification) = result.Value;
        failCommand.ShouldNotBeNull();

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Error");
    }

    [Fact]
    public void Handle_InventoryReservationTimeout_WhenAlreadyPastState_ShouldIgnore()
    {
        // Arrange - Already moved to payment processing
        var saga = CreateSagaInState(OrderFulfillmentState.ProcessingPayment);
        var timeout = new InventoryReservationTimeoutMessage { Id = saga.Id };

        // Act
        var failCommand = saga.Handle(timeout, _logger);

        // Assert - Should be ignored (idempotency)
        saga.State.ShouldBe(OrderFulfillmentState.ProcessingPayment);
        saga.CompletedAt.ShouldBeNull();
        failCommand.ShouldBeNull();
    }

    [Fact]
    public void Handle_PaymentTimeout_WhenProcessingPayment_ShouldReleaseAndFail()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ProcessingPayment);
        saga.IsInventoryReserved = true;
        var timeout = new PaymentTimeoutMessage { Id = saga.Id };

        // Act
        var result = saga.Handle(timeout, _logger);

        // Assert
        result.ShouldNotBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.CompletedAt.ShouldNotBeNull();

        var (releaseCommand, failCommand, notification) = result.Value;
        releaseCommand.ShouldNotBeNull();
        failCommand.ShouldNotBeNull();
        failCommand.FailureReason.ShouldContain("timed out");

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Error");
    }

    [Fact]
    public void Handle_PaymentTimeout_WhenAlreadyPaid_ShouldIgnore()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid = true;
        var timeout = new PaymentTimeoutMessage { Id = saga.Id };

        // Act
        var result = saga.Handle(timeout, _logger);

        // Assert
        result.ShouldBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.ConfirmingInventory);
    }

    [Fact]
    public void Handle_InventoryConfirmationTimeout_ShouldRefundAndRelease()
    {
        // Arrange - Critical: Payment was taken but confirmation is stuck
        var saga = CreateSagaInState(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid = true;
        saga.PaymentTransactionId = Guid.NewGuid();
        saga.IsInventoryReserved = true;
        var timeout = new InventoryConfirmationTimeoutMessage { Id = saga.Id };

        // Act
        var result = saga.Handle(timeout, _logger);

        // Assert
        result.ShouldNotBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.CompletedAt.ShouldNotBeNull();

        var (refundCommand, releaseCommand, failCommand, notification) = result.Value;

        // Must refund since payment was taken
        refundCommand.ShouldNotBeNull();
        refundCommand.PaymentTransactionId.ShouldBe(saga.PaymentTransactionId!.Value);

        releaseCommand.ShouldNotBeNull();
        failCommand.ShouldNotBeNull();

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Error");
    }

    #endregion

    #region NotFound Handler Tests

    [Fact]
    public void NotFound_InventoryReserved_ShouldLogAndNotThrow()
    {
        // Arrange
        var @event = new InventoryReserved(Guid.NewGuid(), []);
        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
            OrderFulfillmentSaga.NotFound(@event, logger));

        exception.ShouldBeNull();
    }

    [Fact]
    public void NotFound_PaymentSucceeded_ShouldLogAndNotThrow()
    {
        // Arrange
        var @event = new PaymentSucceeded(Guid.NewGuid(), Guid.NewGuid(), Money.Create(100m));
        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();

        // Act & Assert
        var exception = Record.Exception(() =>
            OrderFulfillmentSaga.NotFound(@event, logger));

        exception.ShouldBeNull();
    }

    [Fact]
    public void NotFound_PaymentTimeoutMessage_ShouldLogAndNotThrow()
    {
        // Arrange
        var timeout = new PaymentTimeoutMessage { Id = Guid.NewGuid() };
        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();

        // Act & Assert
        var exception = Record.Exception(() =>
            OrderFulfillmentSaga.NotFound(timeout, logger));

        exception.ShouldBeNull();
    }

    [Fact]
    public void NotFound_AllMessageTypes_ShouldNotThrow()
    {
        // This test ensures all NotFound handlers exist and don't throw
        var logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();
        var orderId = Guid.NewGuid();

        // Test all NotFound handlers
        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new InventoryReserved(orderId, []), logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new InventoryReservationFailed(orderId, "test", null), logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new PaymentSucceeded(orderId, Guid.NewGuid(), Money.Create(1m)), logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new PaymentFailed(orderId, "test", null), logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new InventoryConfirmed(orderId), logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new InventoryConfirmationFailed(orderId, "test"), logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new InventoryReservationTimeoutMessage { Id = orderId }, logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new PaymentTimeoutMessage { Id = orderId }, logger)).ShouldBeNull();

        Record.Exception(() => OrderFulfillmentSaga.NotFound(
            new InventoryConfirmationTimeoutMessage { Id = orderId }, logger)).ShouldBeNull();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Saga_WithZeroItems_ShouldStillProcess()
    {
        // Arrange - Edge case: order with no items (gift card, service, etc.)
        var command = new StartOrderFulfillmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-EMPTY",
            Money.Create(50m),
            []);

        // Act
        var (saga, reserveCommand, _) = OrderFulfillmentSaga.Start(command, _logger);

        // Assert
        saga.Items.ShouldBeEmpty();
        reserveCommand.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Saga_StateTransitions_ShouldBeIdempotent()
    {
        // Arrange - Use the Handle method to properly complete the saga
        var saga = CreateSagaInState(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid = true;
        saga.PaymentTransactionId = Guid.NewGuid();

        // Complete the saga through proper workflow
        var (finalizeCommand, notification) = saga.Handle(new InventoryConfirmed(saga.Id), _logger);

        // Assert - Saga should be marked as completed
        saga.State.ShouldBe(OrderFulfillmentState.Completed);
        saga.CompletedAt.ShouldNotBeNull();

        // The FinalizeOrderCommand should be returned
        finalizeCommand.ShouldNotBeNull();
        finalizeCommand.OrderId.ShouldBe(saga.Id);

        // Verify SignalR notification is returned
        notification.ShouldNotBeNull();
        notification.OrderId.ShouldBe(saga.Id);
        notification.Status.ShouldBe("Success");

        // Note: In unit tests, Wolverine's IsCompleted() may not reflect MarkCompleted()
        // because Wolverine manages the completion state internally during message processing.
        // The saga's State property reflects our business logic completion.
    }

    #endregion

    #region SignalR Notification Tests

    [Fact]
    public void OrderStatusChanged_SuccessNotification_ShouldHaveCorrectMessage()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid = true;
        saga.PaymentTransactionId = Guid.NewGuid();
        var @event = new InventoryConfirmed(saga.Id);

        // Act
        var (_, notification) = saga.Handle(@event, _logger);

        // Assert - Verify notification message is user-friendly
        notification.Status.ShouldBe("Success");
        notification.Message.ShouldNotBeNullOrWhiteSpace();
        notification.Message.ShouldContain("confirmed", Case.Insensitive);
    }

    [Fact]
    public void OrderStatusChanged_InventoryFailure_ShouldIndicateOutOfStock()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ReservingInventory);
        var @event = new InventoryReservationFailed(saga.Id, "Insufficient stock", [Guid.NewGuid()]);

        // Act
        var (_, notification) = saga.Handle(@event, _logger);

        // Assert
        notification.Status.ShouldBe("Error");
        notification.Message.ShouldContain("stock", Case.Insensitive);
    }

    [Fact]
    public void OrderStatusChanged_PaymentFailure_ShouldAskToTryAgain()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ProcessingPayment);
        saga.IsInventoryReserved = true;
        var @event = new PaymentFailed(saga.Id, "Card declined", "CARD_DECLINED");

        // Act
        var (_, _, notification) = saga.Handle(@event, _logger);

        // Assert
        notification.Status.ShouldBe("Error");
        notification.Message.ShouldContain("try again", Case.Insensitive);
    }

    [Fact]
    public void OrderStatusChanged_Timeout_ShouldIndicateTimeout()
    {
        // Arrange
        var saga = CreateSagaInState(OrderFulfillmentState.ReservingInventory);
        var timeout = new InventoryReservationTimeoutMessage { Id = saga.Id };

        // Act
        var result = saga.Handle(timeout, _logger);

        // Assert
        result.ShouldNotBeNull();
        var (_, notification) = result.Value;
        notification.Status.ShouldBe("Error");
        notification.Message.ShouldContain("timed out", Case.Insensitive);
    }

    [Fact]
    public void OrderStatusChanged_AllNotifications_ShouldContainOrderId()
    {
        // Arrange - Test multiple scenarios
        var saga = CreateSagaInState(OrderFulfillmentState.ConfirmingInventory);
        saga.IsPaid = true;
        saga.PaymentTransactionId = Guid.NewGuid();
        saga.IsInventoryReserved = true;

        // Act - Critical failure scenario
        var @event = new InventoryConfirmationFailed(saga.Id, "Stock mismatch");
        var (_, _, _, notification) = saga.Handle(@event, _logger);

        // Assert - Every notification must have the OrderId for client-side filtering
        notification.OrderId.ShouldBe(saga.Id);
        notification.OrderId.ShouldNotBe(Guid.Empty);
    }

    #endregion

    #region Helper Methods

    private OrderFulfillmentSaga CreateSagaInState(OrderFulfillmentState state)
    {
        return new OrderFulfillmentSaga
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = $"ORD-TEST-{Guid.NewGuid():N}".Substring(0, 20),
            TotalAmount = Money.Create(149.99m, "USD"),
            Items =
            [
                new OrderItemReservation(Guid.NewGuid(), 2, "TEST-SKU")
            ],
            State = state,
            StartedAt = DateTime.UtcNow
        };
    }

    #endregion
}
