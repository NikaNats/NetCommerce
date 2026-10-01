#region

using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;

#endregion

namespace NetCommerce.Domain.Tests.Ordering;

/// <summary>
///     Tests for customer/admin cancellation (CancelOrderFulfillmentCommand), the payment-initiated
///     bookkeeping step, and the strict timeout guards that make cancellation safe.
///     Cancellation is the saga's most branching handler (paid vs unpaid, five releasable
///     states, conditional stall timeout) and previously had no direct coverage despite living
///     in the highest-risk file of the codebase.
/// </summary>
public class SagaCancellationTests
{
    private readonly ILogger<OrderFulfillmentSaga> _logger;

    public SagaCancellationTests()
    {
        _logger = Substitute.For<ILogger<OrderFulfillmentSaga>>();
    }

    private static OrderFulfillmentSaga CreateSaga(
        OrderFulfillmentState state,
        bool paid = false,
        string? paymentTransactionId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OrderNumber = "ORD-20240101-00001",
            TotalAmount = new Money(100m, "USD"),
            PaymentMethodId = "pm_test",
            Items = [],
            State = state,
            IsInventoryReserved = true,
            IsPaid = paid,
            PaymentTransactionId = paymentTransactionId,
            StartedAt = DateTime.UtcNow
        };

    [Fact]
    public void Cancel_UnpaidOrderInGracePeriod_ShouldReleaseCompleteAndNotify()
    {
        // Arrange: reserved but never charged — nothing to refund.
        var saga = CreateSaga(OrderFulfillmentState.InGracePeriod);
        var command = new CancelOrderFulfillmentCommand(saga.Id, "changed my mind");

        // Act
        var result = saga.Handle(command, _logger);

        // Assert: release for the held stock, immediate completion, no refund machinery.
        result.ReleaseCommand.ShouldNotBeNull();
        result.ReleaseCommand.OrderId.ShouldBe(saga.Id);
        result.RefundCommand.ShouldBeNull();
        result.StallTimeout.ShouldBeNull();
        result.FailCommand.FailureReason.ShouldBe("changed my mind");
        result.Notification.ShouldNotBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.FailureReason.ShouldNotBeNull();
        saga.FailureReason.ShouldContain("cancelled");
        saga.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Cancel_PaidOrder_ShouldRefundAndParkInCompensating()
    {
        // Arrange: money already captured — Guarded Compensation engages.
        var saga = CreateSaga(OrderFulfillmentState.ProcessingPayment, paid: true, paymentTransactionId: "pi_captured");
        var command = new CancelOrderFulfillmentCommand(saga.Id, "duplicate order");

        // Act
        var result = saga.Handle(command, _logger);

        // Assert: refund + stall guard scheduled, saga stays alive awaiting verification.
        result.RefundCommand.ShouldNotBeNull();
        result.RefundCommand.PaymentTransactionId.ShouldBe("pi_captured");
        result.RefundCommand.Amount.ShouldBe(saga.TotalAmount);
        result.ReleaseCommand.ShouldNotBeNull();
        result.StallTimeout.ShouldNotBeNull();
        result.StallTimeout.Id.ShouldBe(saga.Id);
        saga.State.ShouldBe(OrderFulfillmentState.Compensating);
        saga.CompletedAt.ShouldBeNull();
        saga.FailureReason.ShouldNotBeNull();
        saga.FailureReason.ShouldContain("cancelled after payment");
    }

    [Fact]
    public void Cancel_PaidButMissingTransactionId_ShouldCompleteWithoutRefund()
    {
        // Arrange: paid flag set but no PSP reference — there is nothing refundable.
        var saga = CreateSaga(OrderFulfillmentState.ConfirmingInventory, paid: true, paymentTransactionId: null);
        var command = new CancelOrderFulfillmentCommand(saga.Id, "duplicate order");

        // Act
        var result = saga.Handle(command, _logger);

        // Assert
        result.RefundCommand.ShouldBeNull();
        result.StallTimeout.ShouldBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
        saga.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Cancel_WhenAlreadyTerminal_ShouldNotReleaseOrRefund()
    {
        // Arrange: nothing held (already failed) — cancellation is a no-op for resources.
        var saga = CreateSaga(OrderFulfillmentState.Failed);
        var command = new CancelOrderFulfillmentCommand(saga.Id, "late cancel");

        // Act
        var result = saga.Handle(command, _logger);

        // Assert
        result.ReleaseCommand.ShouldBeNull();
        result.RefundCommand.ShouldBeNull();
        result.StallTimeout.ShouldBeNull();
        result.FailCommand.ShouldNotBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
    }

    [Fact]
    public void Handle_PaymentInitiated_ShouldRecordTransactionIdWithoutStateChange()
    {
        // Arrange
        var saga = CreateSaga(OrderFulfillmentState.ProcessingPayment);
        var @event = new PaymentInitiated(saga.Id, Guid.NewGuid(), "pi_ext_9", saga.TotalAmount);

        // Act
        saga.Handle(@event, _logger);

        // Assert: bookkeeping only — the saga still awaits the webhook.
        saga.PaymentTransactionId.ShouldBe("pi_ext_9");
        saga.State.ShouldBe(OrderFulfillmentState.ProcessingPayment);
        saga.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void Handle_GracePeriodTimeout_WhenCancelled_ShouldIgnore()
    {
        // Arrange: the late-timer path that would otherwise double-charge a cancelled order.
        var saga = CreateSaga(OrderFulfillmentState.Failed);
        var timeout = new GracePeriodTimeout { Id = saga.Id };

        // Act
        var (lockCommand, notification) = saga.Handle(timeout, _logger);

        // Assert
        lockCommand.ShouldBeNull();
        notification.ShouldBeNull();
        saga.State.ShouldBe(OrderFulfillmentState.Failed);
    }
}
