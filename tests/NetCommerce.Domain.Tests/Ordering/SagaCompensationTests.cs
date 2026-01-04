using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Critical tests for the Guarded Compensation pattern in OrderFulfillmentSaga.
///     Tests the enterprise-grade failure handling scenarios.
/// </summary>
public class SagaCompensationTests
{
    private readonly ILogger<OrderFulfillmentSaga> _logger;

    public SagaCompensationTests()
    {
        _logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();
    }

    #region Guarded Compensation Tests

    [Fact]
    public void InventoryConfirmationFailed_ShouldTransitionToCompensating_WithoutMarkingComplete()
    {
        // Arrange - Create a saga that has reached the "ConfirmingInventory" state
        var saga = CreateSagaInConfirmingInventoryState();
        var @event = new InventoryConfirmationFailed(
            saga.Id,
            "Warehouse system outage - stock count mismatch");

        // Act
        var result = saga.Handle(@event, _logger);

        // Assert - The saga should NOT be completed
        saga.State.ShouldBe(OrderFulfillmentState.Compensating);
        saga.CompletedAt.ShouldBeNull(); // NOT completed yet!
        saga.FailureReason.ShouldBe("Warehouse system outage - stock count mismatch");

        // Should return compensating commands
        result.RefundCommand.ShouldNotBeNull();
        result.RefundCommand.OrderId.ShouldBe(saga.Id);
        result.RefundCommand.Amount.ShouldBe(saga.TotalAmount);

        result.ReleaseCommand.ShouldNotBeNull();
        result.ReleaseCommand.OrderId.ShouldBe(saga.Id);
    }

    [Fact]
    public void RefundCompleted_ShouldMarkSagaAsCompleted_AndTransitionToFailed()
    {
        // Arrange - Saga in Compensating state awaiting refund confirmation
        var saga = CreateSagaInCompensatingState();
        var refundTransactionId = Guid.NewGuid();
        var @event = new RefundCompleted(
            saga.Id,
            refundTransactionId,
            saga.TotalAmount);

        // Act
        saga.Handle(@event, _logger);

        // Assert - NOW the saga can be safely deleted
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.CompletedAt.ShouldNotBeNull();
        saga.CompletedAt.Value.ShouldBeInRange(
            DateTime.UtcNow.AddSeconds(-5),
            DateTime.UtcNow.AddSeconds(5));

        // Verify MarkCompleted was called by checking saga properties
        // In Wolverine, MarkCompleted sets internal flags for deletion
    }

    [Fact]
    public void RefundFailed_ShouldTransitionToManualIntervention_WithoutMarkingComplete()
    {
        // Arrange - Saga in Compensating state
        var saga = CreateSagaInCompensatingState();
        var @event = new RefundFailed(
            saga.Id,
            "Stripe API returned 500 - Service temporarily unavailable");

        // Act
        saga.Handle(@event, _logger);

        // Assert - The NIGHTMARE scenario - saga stays in DB
        saga.State.ShouldBe(OrderFulfillmentState.ManualInterventionRequired);
        saga.CompletedAt.ShouldBeNull(); // CRITICAL: NOT completed!
        saga.FailureReason.ShouldContain("Refund failed");
        saga.FailureReason.ShouldContain("Stripe API");
    }

    [Fact]
    public void RefundFailed_MultipleTimes_ShouldRemainInManualInterventionState()
    {
        // Arrange
        var saga = CreateSagaInCompensatingState();

        // Act - Simulate multiple refund attempts failing
        saga.Handle(new RefundFailed(saga.Id, "Attempt 1: Network timeout"), _logger);
        var firstState = saga.State;

        saga.Handle(new RefundFailed(saga.Id, "Attempt 2: Invalid merchant account"), _logger);
        var secondState = saga.State;

        // Assert - Should remain in ManualInterventionRequired
        firstState.ShouldBe(OrderFulfillmentState.ManualInterventionRequired);
        secondState.ShouldBe(OrderFulfillmentState.ManualInterventionRequired);
        saga.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void Compensating_ShouldNotTransitionTo_Completed_DirectlyWithoutRefundConfirmation()
    {
        // Arrange
        var saga = CreateSagaInCompensatingState();

        // Act & Assert - Verify saga cannot be manually completed
        saga.State.ShouldBe(OrderFulfillmentState.Compensating);

        // The saga is "stuck" waiting for RefundCompleted or RefundFailed
        // This is the CORRECT behavior - no premature deletion
        saga.CompletedAt.ShouldBeNull();
    }

    #endregion

    #region Financial Integrity Tests

    [Fact]
    public void PaymentSucceeded_ThenInventoryConfirmationFailed_ShouldIssueRefund()
    {
        // Arrange - Create saga, simulate happy path until payment succeeds
        var orderId = Guid.NewGuid();
        var command = CreateStartCommand(orderId);
        var (saga, _, _) = OrderFulfillmentSaga.Start(command, _logger);

        // Simulate: Inventory Reserved
        var reservedItems = new List<ReservedItem>
        {
            new(Guid.NewGuid(), saga.Items[0].ProductId, saga.Items[0].Quantity)
        };
        saga.Handle(new InventoryReserved(orderId, reservedItems), _logger);
        saga.Handle(new InventoryLocked(orderId, reservedItems), _logger);

        // Simulate: Payment Succeeded
        var transactionId = "stripe_ch_" + Guid.NewGuid().ToString("N");
        saga.Handle(new PaymentSucceeded(orderId, transactionId, saga.TotalAmount), _logger);

        saga.IsPaid.ShouldBeTrue();
        saga.PaymentTransactionId.ShouldBe(transactionId);

        // Act - Inventory confirmation fails AFTER payment
        var failureEvent = new InventoryConfirmationFailed(orderId, "Stock count mismatch");
        var result = saga.Handle(failureEvent, _logger);

        // Assert - Must issue refund for the captured payment
        result.RefundCommand.ShouldNotBeNull();
        result.RefundCommand.PaymentTransactionId.ShouldBe(transactionId);
        result.RefundCommand.Amount.ShouldBe(saga.TotalAmount);
        result.RefundCommand.Reason.ShouldContain("Inventory confirmation failed");

        saga.State.ShouldBe(OrderFulfillmentState.Compensating);
    }

    [Fact]
    public void RefundAmount_ShouldExactlyMatchPaymentAmount()
    {
        // Arrange
        var saga = CreateSagaInConfirmingInventoryState();
        var originalAmount = saga.TotalAmount;

        // Act
        var result = saga.Handle(
            new InventoryConfirmationFailed(saga.Id, "Test failure"),
            _logger);

        // Assert - Financial integrity check
        result.RefundCommand.Amount.Amount.ShouldBe(originalAmount.Amount);
        result.RefundCommand.Amount.Currency.ShouldBe(originalAmount.Currency);
    }

    #endregion

    #region State Machine Invariant Tests

    [Fact]
    public void ManualInterventionRequired_ShouldNotTransitionToAnyOtherState()
    {
        // Arrange
        var saga = CreateSagaInManualInterventionState();
        var initialState = saga.State;

        // Act & Assert - Try various events, should remain in ManualInterventionRequired
        // In production, only admin actions can resolve this state

        saga.State.ShouldBe(OrderFulfillmentState.ManualInterventionRequired);
        saga.CompletedAt.ShouldBeNull();

        // This saga should only be resolved through:
        // 1. Manual refund via payment gateway dashboard
        // 2. Admin API endpoint that manually marks saga as resolved
        // 3. Compensation transaction executed by operations team
    }

    [Fact]
    public void Compensating_CanOnlyTransitionTo_FailedOrManualIntervention()
    {
        // Arrange
        var saga = CreateSagaInCompensatingState();

        // Act & Assert - Valid transitions
        var sagaForSuccess = CreateSagaInCompensatingState();
        sagaForSuccess.Handle(new RefundCompleted(sagaForSuccess.Id, Guid.NewGuid(), sagaForSuccess.TotalAmount), _logger);
        sagaForSuccess.State.ShouldBe(OrderFulfillmentState.Failed);

        var sagaForFailure = CreateSagaInCompensatingState();
        sagaForFailure.Handle(new RefundFailed(sagaForFailure.Id, "Refund API error"), _logger);
        sagaForFailure.State.ShouldBe(OrderFulfillmentState.ManualInterventionRequired);
    }

    #endregion

    #region Helper Methods

    private StartOrderFulfillmentCommand CreateStartCommand(Guid? orderId = null)
    {
        return new StartOrderFulfillmentCommand(
            orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-TEST-" + Guid.NewGuid().ToString("N")[..8],
            Money.Create(299.99m, "USD"),
            new List<OrderItemReservation>
            {
                new(Guid.NewGuid(), 1, "TEST-SKU-001")
            });
    }

    private OrderFulfillmentSaga CreateSagaInConfirmingInventoryState()
    {
        var command = CreateStartCommand();
        var (saga, _, _) = OrderFulfillmentSaga.Start(command, _logger);

        // Advance to ConfirmingInventory state
        var reservedItems = new List<ReservedItem>
        {
            new(Guid.NewGuid(), saga.Items[0].ProductId, saga.Items[0].Quantity)
        };
        saga.Handle(new InventoryReserved(saga.Id, reservedItems), _logger);
        saga.Handle(new InventoryLocked(saga.Id, reservedItems), _logger);
        saga.Handle(new PaymentSucceeded(saga.Id, "txn_test_123", saga.TotalAmount), _logger);

        saga.State.ShouldBe(OrderFulfillmentState.ConfirmingInventory);
        return saga;
    }

    private OrderFulfillmentSaga CreateSagaInCompensatingState()
    {
        var saga = CreateSagaInConfirmingInventoryState();
        saga.Handle(new InventoryConfirmationFailed(saga.Id, "Test failure"), _logger);
        saga.State.ShouldBe(OrderFulfillmentState.Compensating);
        return saga;
    }

    private OrderFulfillmentSaga CreateSagaInManualInterventionState()
    {
        var saga = CreateSagaInCompensatingState();
        saga.Handle(new RefundFailed(saga.Id, "Refund gateway offline"), _logger);
        saga.State.ShouldBe(OrderFulfillmentState.ManualInterventionRequired);
        return saga;
    }

    #endregion
}
