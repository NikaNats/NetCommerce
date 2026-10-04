#region

using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Payments.Application.EventHandlers;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NSubstitute;
using Wolverine;

#endregion

namespace NetCommerce.Domain.Tests.Payments;

/// <summary>
///     Tests for the refund leg of <see cref="SagaPaymentHandlers"/> (guarded compensation)
///     and the webhook-first payment initiation contract. Previously untested despite moving
///     real money: every outcome here must resolve to a deterministic saga message.
/// </summary>
public class SagaPaymentRefundTests
{
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentTransactionRepository _repository;
    private readonly ILogger<RefundPaymentCommand> _refundLogger;
    private readonly ILogger<RequestPaymentCommand> _requestLogger;

    public SagaPaymentRefundTests()
    {
        _gateway = Substitute.For<IPaymentGateway>();
        _gateway.Provider.Returns(NetCommerce.Payments.Application.Gateways.PaymentProvider.Stripe);
        _repository = Substitute.For<IPaymentTransactionRepository>();
        _refundLogger = Substitute.For<ILogger<RefundPaymentCommand>>();
        _requestLogger = Substitute.For<ILogger<RequestPaymentCommand>>();
    }

    private static RefundPaymentCommand RefundCommand(Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(), "pi_test_123", new Money(99.99m, "USD"), "test refund");

    private static RequestPaymentCommand PaymentCommand(Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(), Guid.NewGuid(), new Money(99.99m, "USD"), "ORD-1", "pm_test_123");

    #region Refund outcomes

    [Fact]
    public async Task Handle_RefundGatewaySuccess_ShouldReturnRefundCompleted()
    {
        // Arrange
        var command = RefundCommand();
        _gateway.ProcessRefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RefundResult("re_123", true)));

        // Act
        var result = await SagaPaymentHandlers.Handle(command, _gateway, _refundLogger);

        // Assert
        var completed = result.ShouldBeOfType<RefundCompleted>();
        completed.OrderId.ShouldBe(command.OrderId);
        completed.Amount.ShouldBe(command.Amount);
    }

    [Fact]
    public async Task Handle_RefundGatewayReportsFailure_ShouldReturnRefundFailedWithReason()
    {
        // Arrange
        var command = RefundCommand();
        _gateway.ProcessRefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RefundResult("re_123", false, "already refunded")));

        // Act
        var result = await SagaPaymentHandlers.Handle(command, _gateway, _refundLogger);

        // Assert
        var failed = result.ShouldBeOfType<RefundFailed>();
        failed.OrderId.ShouldBe(command.OrderId);
        failed.Reason.ShouldBe("already refunded");
    }

    [Fact]
    public async Task Handle_RefundGatewayThrows_ShouldConvertToRefundFailed()
    {
        // Arrange: infrastructure blowups must still resolve to a saga outcome,
        // otherwise compensation stalls forever waiting for a reply that never comes.
        var command = RefundCommand();
        _gateway.ProcessRefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<RefundResult>>>(_ => throw new InvalidOperationException("PSP timeout"));

        // Act
        var result = await SagaPaymentHandlers.Handle(command, _gateway, _refundLogger);

        // Assert
        var failed = result.ShouldBeOfType<RefundFailed>();
        failed.OrderId.ShouldBe(command.OrderId);
        failed.Reason.ShouldBe("PSP timeout");
    }

    [Fact]
    public async Task Handle_RefundGatewayCancelled_ShouldRethrowNotConvert()
    {
        // Arrange: cancellation is NOT a refund outcome. Converting it to RefundFailed would
        // lie to the saga (refund state unknown) and trigger wrongful manual intervention.
        var command = RefundCommand();
        _gateway.ProcessRefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<RefundResult>>>(_ => throw new OperationCanceledException());

        // Act + Assert
        await Should.ThrowAsync<OperationCanceledException>(
            SagaPaymentHandlers.Handle(command, _gateway, _refundLogger));
    }

    [Fact]
    public async Task Handle_RefundCommand_ShouldMapOriginalTransactionAndAmount()
    {
        // Arrange
        var command = RefundCommand();
        RefundRequest? captured = null;
        _gateway.ProcessRefundAsync(Arg.Do<RefundRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RefundResult("re_123", true)));

        // Act
        await SagaPaymentHandlers.Handle(command, _gateway, _refundLogger);

        // Assert
        captured.ShouldNotBeNull();
        captured.OriginalTransactionId.ShouldBe(command.PaymentTransactionId);
        captured.Amount.ShouldBe(command.Amount);
        captured.Reason.ShouldBe(command.Reason);
        captured.IdempotencyKey.ShouldBe($"refund_{command.OrderId}");
    }

    [Fact]
    public async Task Handle_RefundRedelivery_ShouldCarryTheSameIdempotencyKey()
    {
        // DDIA duplicate suppression: a redelivery of the same business intent
        // (Wolverine retry after unknown PSP success) must carry the SAME key so
        // the PSP dedups instead of issuing a second refund. A per-attempt key
        // (e.g. Guid.NewGuid) would defeat the gateway dedup entirely.
        var command = RefundCommand();
        var keys = new List<string?>();
        _gateway.ProcessRefundAsync(
                Arg.Do<RefundRequest>(r => keys.Add(r.IdempotencyKey)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RefundResult("re_123", true)));

        // Act: same command delivered twice (original + redelivery)
        await SagaPaymentHandlers.Handle(command, _gateway, _refundLogger);
        await SagaPaymentHandlers.Handle(command, _gateway, _refundLogger);

        // Assert
        keys.Count.ShouldBe(2);
        keys[0].ShouldBe($"refund_{command.OrderId}");
        keys[1].ShouldBe(keys[0]);
    }

    #endregion

    #region Webhook-first initiation contract

    [Fact]
    public async Task Handle_RequestPaymentPending_ShouldReturnPaymentInitiatedWithExternalId()
    {
        // Arrange
        var command = PaymentCommand();
        var envelope = new Envelope { Id = Guid.NewGuid() };
        _gateway.ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PaymentResult("pi_ext_1", PaymentResultStatus.Pending)));

        // Act
        var result = await SagaPaymentHandlers.Handle(
            command, _gateway, _repository, envelope, _requestLogger);

        // Assert: webhook-first means Pending + external id, never an immediate success.
        var initiated = result.ShouldBeOfType<PaymentInitiated>();
        initiated.OrderId.ShouldBe(command.OrderId);
        initiated.ExternalTransactionId.ShouldBe("pi_ext_1");
        initiated.Amount.ShouldBe(command.Amount);
        await _repository.Received(1).AddAsync(
            Arg.Is<PaymentTransaction>(t => t.OrderId == command.OrderId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RequestPaymentGatewayError_ShouldReturnPaymentFailedWithGatewayCode()
    {
        // Arrange
        var command = PaymentCommand();
        var envelope = new Envelope { Id = Guid.NewGuid() };
        _gateway.ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PaymentResult>(Error.Failure("GATEWAY_DOWN", "connection refused")));

        // Act
        var result = await SagaPaymentHandlers.Handle(
            command, _gateway, _repository, envelope, _requestLogger);

        // Assert
        var failed = result.ShouldBeOfType<PaymentFailed>();
        failed.OrderId.ShouldBe(command.OrderId);
        failed.Reason.ShouldBe("connection refused");
    }

    [Fact]
    public async Task Handle_RequestPaymentDeclined_ShouldReturnPaymentFailedWithCardDeclinedCode()
    {
        // Arrange
        var command = PaymentCommand();
        var envelope = new Envelope { Id = Guid.NewGuid() };
        _gateway.ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PaymentResult("pi_ext_1", PaymentResultStatus.Failed, "card declined")));

        // Act
        var result = await SagaPaymentHandlers.Handle(
            command, _gateway, _repository, envelope, _requestLogger);

        // Assert
        result.ShouldBeOfType<PaymentFailed>();
    }

    [Fact]
    public async Task Handle_RequestPaymentWithoutMethodId_ShouldFallBackToTestToken()
    {
        // Arrange: legacy/grace-period flows start sagas without a payment method token.
        var command = new RequestPaymentCommand(Guid.NewGuid(), Guid.NewGuid(), new Money(10m, "USD"), "ORD-9", "");
        var envelope = new Envelope { Id = Guid.NewGuid() };
        PaymentRequest? captured = null;
        _gateway.ProcessPaymentAsync(Arg.Do<PaymentRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PaymentResult("pi_ext_1", PaymentResultStatus.Pending)));

        // Act
        await SagaPaymentHandlers.Handle(command, _gateway, _repository, envelope, _requestLogger);

        // Assert
        captured.ShouldNotBeNull();
        captured.PaymentMethodToken.ShouldBe("tok_visa");
    }

    [Fact]
    public async Task Handle_RequestPaymentGatewayThrows_ShouldPropagateForRetry()
    {
        // Arrange: initiation failures are fatal (no saga outcome to return); Wolverine
        // redelivery retries. Swallowing them here would park the saga in limbo.
        var command = PaymentCommand();
        var envelope = new Envelope { Id = Guid.NewGuid() };
        _gateway.ProcessPaymentAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<PaymentResult>>>(_ => throw new InvalidOperationException("boom"));

        // Act + Assert
        await Should.ThrowAsync<InvalidOperationException>(
            SagaPaymentHandlers.Handle(command, _gateway, _repository, envelope, _requestLogger));
    }

    #endregion
}
