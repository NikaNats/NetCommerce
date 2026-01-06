#region

using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Handlers;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;

#endregion

namespace NetCommerce.Domain.Tests.Payments;

/// <summary>
///     Unit tests for ProcessExternalPaymentConfirmationHandler.
///     Tests idempotency, status updates, and domain event publishing.
/// </summary>
public class ProcessExternalPaymentConfirmationHandlerTests
{
    private readonly ILogger _mockLogger;
    private readonly IPaymentTransactionRepository _mockRepository;

    public ProcessExternalPaymentConfirmationHandlerTests()
    {
        _mockRepository = Substitute.For<IPaymentTransactionRepository>();
        _mockLogger = Substitute.For<ILogger>();
    }

    [Fact]
    public async Task Handle_SuccessfulPayment_ShouldMarkAsCompleted()
    {
        // Arrange
        string externalTransactionId = "pi_test_123";
        var orderId = Guid.NewGuid();
        PaymentTransaction payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Succeeded",
            "evt_123");

        // Act
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command,
            _mockRepository,
            _mockLogger,
            default);

        // Assert
        payment.Status.ShouldBe(PaymentStatus.Completed);
        payment.CompletedAt.ShouldNotBeNull();
        _mockRepository.Received(1).Update(payment);
    }

    [Fact]
    public async Task Handle_FailedPayment_ShouldMarkAsFailed()
    {
        // Arrange
        string externalTransactionId = "pi_test_failed_123";
        var orderId = Guid.NewGuid();
        PaymentTransaction payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Failed",
            "evt_456");

        // Act
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command,
            _mockRepository,
            _mockLogger,
            default);

        // Assert
        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureReason!.ShouldContain("Failed");
        _mockRepository.Received(1).Update(payment);
    }

    [Fact]
    public async Task Handle_AlreadyCompletedPayment_ShouldBeIdempotent()
    {
        // Arrange - Payment already completed
        string externalTransactionId = "pi_test_already_completed";
        var orderId = Guid.NewGuid();
        PaymentTransaction payment = CreateCompletedPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Succeeded",
            "evt_duplicate_789");

        // Act
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command,
            _mockRepository,
            _mockLogger,
            default);

        // Assert - Should NOT update payment (idempotency)
        _mockRepository.DidNotReceive().Update(Arg.Any<PaymentTransaction>());

        // Should log idempotency message
        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("already completed")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_AlreadyFailedPayment_ShouldBeIdempotent()
    {
        // Arrange - Payment already failed
        string externalTransactionId = "pi_test_already_failed";
        var orderId = Guid.NewGuid();
        PaymentTransaction payment = CreateFailedPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Failed",
            "evt_duplicate_fail");

        // Act
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command,
            _mockRepository,
            _mockLogger,
            default);

        // Assert - Should NOT update payment (idempotency)
        _mockRepository.DidNotReceive().Update(Arg.Any<PaymentTransaction>());
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ShouldLogWarningAndReturn()
    {
        // Arrange - Payment doesn't exist (webhook for old deployment or test)
        string externalTransactionId = "pi_test_not_found";

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns((PaymentTransaction?)null);

        var command = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Succeeded",
            "evt_not_found");

        // Act
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command,
            _mockRepository,
            _mockLogger,
            default);

        // Assert - Should NOT throw, just log warning
        _mockRepository.DidNotReceive().Update(Arg.Any<PaymentTransaction>());

        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Payment not found")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_CanceledPayment_ShouldMarkAsFailed()
    {
        // Arrange
        string externalTransactionId = "pi_test_canceled_123";
        var orderId = Guid.NewGuid();
        PaymentTransaction payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Canceled",
            "evt_cancel");

        // Act
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command,
            _mockRepository,
            _mockLogger,
            default);

        // Assert
        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureReason!.ShouldContain("Canceled");
        _mockRepository.Received(1).Update(payment);
    }

    [Fact]
    public async Task Handle_UnknownStatus_ShouldLogWarningAndNotUpdate()
    {
        // Arrange
        string externalTransactionId = "pi_test_unknown_status";
        var orderId = Guid.NewGuid();
        PaymentTransaction payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "UnknownStatus",
            "evt_unknown");

        // Act
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command,
            _mockRepository,
            _mockLogger,
            default);

        // Assert - Should log warning but not update
        payment.Status.ShouldBe(PaymentStatus.Pending);
        _mockRepository.DidNotReceive().Update(Arg.Any<PaymentTransaction>());

        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Unknown webhook status")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Handle_MultipleSuccessWebhooks_ShouldProcessOnlyFirst()
    {
        // Arrange - Simulate Stripe retry scenario
        string externalTransactionId = "pi_test_retry_123";
        var orderId = Guid.NewGuid();
        PaymentTransaction payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId)
            .Returns(payment);

        var command1 = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Succeeded",
            "evt_first");

        var command2 = new ProcessExternalPaymentConfirmation(
            externalTransactionId,
            "Succeeded",
            "evt_retry");

        // Act - Process first webhook
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command1,
            _mockRepository,
            _mockLogger,
            default);

        // Manually mark as completed (simulating domain event processing)
        payment.MarkAsCompleted(externalTransactionId);

        // Act - Process second webhook (retry)
        await ProcessExternalPaymentConfirmationHandler.Handle(
            command2,
            _mockRepository,
            _mockLogger,
            default);

        // Assert - Update called only once
        _mockRepository.Received(1).Update(payment);
    }

    #region Helper Methods

    private PaymentTransaction CreatePendingPayment(Guid orderId, string externalTransactionId)
    {
        var payment = PaymentTransaction.Create(
            orderId,
            Money.Create(100m, "USD"),
            PaymentProvider.Stripe,
            $"idempotency_{Guid.NewGuid()}");

        payment.SetExternalTransactionId(externalTransactionId);

        return payment;
    }

    private PaymentTransaction CreateCompletedPayment(Guid orderId, string externalTransactionId)
    {
        PaymentTransaction payment = CreatePendingPayment(orderId, externalTransactionId);
        payment.MarkAsCompleted(externalTransactionId);
        return payment;
    }

    private PaymentTransaction CreateFailedPayment(Guid orderId, string externalTransactionId)
    {
        PaymentTransaction payment = CreatePendingPayment(orderId, externalTransactionId);
        payment.MarkAsFailed("Card declined");
        return payment;
    }

    #endregion
}
