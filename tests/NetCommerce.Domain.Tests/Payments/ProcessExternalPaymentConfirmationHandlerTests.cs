#nullable enable
using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Handlers;
using NetCommerce.SharedKernel.Events;
using NSubstitute;
using Shouldly;
using Xunit;

namespace NetCommerce.Domain.Tests.Payments;

/// <summary>
/// Unit tests for ProcessExternalPaymentConfirmationHandler.
/// Tests idempotency, status updates, and domain event publishing.
/// </summary>
public class ProcessExternalPaymentConfirmationHandlerTests
{
    private readonly IPaymentTransactionRepository _mockRepository;
    private readonly ILogger _mockLogger;

    public ProcessExternalPaymentConfirmationHandlerTests()
    {
        _mockRepository = Substitute.For<IPaymentTransactionRepository>();
        _mockLogger = Substitute.For<ILogger>();
    }

    [Fact]
    public async Task Handle_SuccessfulPayment_ShouldMarkAsCompleted()
    {
        // Arrange
        var externalTransactionId = "pi_test_123";
        var orderId = Guid.NewGuid();
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Succeeded",
            WebhookEventId: "evt_123");

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
        var externalTransactionId = "pi_test_failed_123";
        var orderId = Guid.NewGuid();
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Failed",
            WebhookEventId: "evt_456");

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
        var externalTransactionId = "pi_test_already_completed";
        var orderId = Guid.NewGuid();
        var payment = CreateCompletedPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Succeeded",
            WebhookEventId: "evt_duplicate_789");

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
        var externalTransactionId = "pi_test_already_failed";
        var orderId = Guid.NewGuid();
        var payment = CreateFailedPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Failed",
            WebhookEventId: "evt_duplicate_fail");

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
        var externalTransactionId = "pi_test_not_found";

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns((PaymentTransaction?)null);

        var command = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Succeeded",
            WebhookEventId: "evt_not_found");

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
        var externalTransactionId = "pi_test_canceled_123";
        var orderId = Guid.NewGuid();
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Canceled",
            WebhookEventId: "evt_cancel");

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
        var externalTransactionId = "pi_test_unknown_status";
        var orderId = Guid.NewGuid();
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns(payment);

        var command = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "UnknownStatus",
            WebhookEventId: "evt_unknown");

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
        var externalTransactionId = "pi_test_retry_123";
        var orderId = Guid.NewGuid();
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetByExternalIdAsync(externalTransactionId, default)
            .Returns(payment);

        var command1 = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Succeeded",
            WebhookEventId: "evt_first");

        var command2 = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: externalTransactionId,
            Status: "Succeeded",
            WebhookEventId: "evt_retry");

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
            NetCommerce.SharedKernel.Domain.Money.Create(100m, "USD"),
            PaymentProvider.Stripe,
            $"idempotency_{Guid.NewGuid()}");

        payment.SetExternalTransactionId(externalTransactionId);

        return payment;
    }

    private PaymentTransaction CreateCompletedPayment(Guid orderId, string externalTransactionId)
    {
        var payment = CreatePendingPayment(orderId, externalTransactionId);
        payment.MarkAsCompleted(externalTransactionId);
        return payment;
    }

    private PaymentTransaction CreateFailedPayment(Guid orderId, string externalTransactionId)
    {
        var payment = CreatePendingPayment(orderId, externalTransactionId);
        payment.MarkAsFailed("Card declined");
        return payment;
    }

    #endregion
}
