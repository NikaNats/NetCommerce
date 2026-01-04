#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.BackgroundJobs;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using NetCommerce.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using Wolverine;
using Xunit;
using PaymentProviderDomain = NetCommerce.Payments.Domain.Transactions.PaymentProvider;

namespace NetCommerce.Domain.Tests.Payments;

/// <summary>
/// Unit tests for PaymentReconciliationJob.
/// Tests the safety net for missed/delayed webhooks.
/// </summary>
public class PaymentReconciliationJobTests
{
    private readonly IServiceProvider _mockServiceProvider;
    private readonly IServiceScope _mockScope;
    private readonly IServiceScopeFactory _mockScopeFactory;
    private readonly IPaymentTransactionRepository _mockRepository;
    private readonly IPaymentGateway _mockGateway;
    private readonly IMessageBus _mockBus;
    private readonly ILogger<PaymentReconciliationJob> _mockLogger;

    public PaymentReconciliationJobTests()
    {
        _mockRepository = Substitute.For<IPaymentTransactionRepository>();
        _mockGateway = Substitute.For<IPaymentGateway>();
        _mockBus = Substitute.For<IMessageBus>();
        _mockLogger = Substitute.For<ILogger<PaymentReconciliationJob>>();

        // Create a mock service provider for the scope
        var mockScopeServiceProvider = Substitute.For<IServiceProvider>();
        mockScopeServiceProvider.GetService(typeof(IPaymentTransactionRepository))
            .Returns(_mockRepository);
        mockScopeServiceProvider.GetService(typeof(IPaymentGateway))
            .Returns(_mockGateway);
        mockScopeServiceProvider.GetService(typeof(IMessageBus))
            .Returns(_mockBus);

        _mockScope = Substitute.For<IServiceScope>();
        _mockScope.ServiceProvider.Returns(mockScopeServiceProvider);

        _mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        _mockScopeFactory.CreateScope().Returns(_mockScope);

        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockServiceProvider.GetService(typeof(IServiceScopeFactory))
            .Returns(_mockScopeFactory);
    }

    [Fact]
    public async Task ReconcilePayments_NoPendingPayments_ShouldNotCallGateway()
    {
        // Arrange
        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction>());

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act - Trigger reconciliation via reflection (private method)
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert
        await _mockGateway.DidNotReceive().GetPaymentStatusAsync(Arg.Any<string>(), default);
        await _mockBus.DidNotReceive().InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcilePayments_PaymentSucceededButWebhookMissed_ShouldDispatchConfirmation()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var externalTransactionId = "pi_test_reconcile_success";
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction> { payment });

        // Gateway reports payment succeeded
        _mockGateway.GetPaymentStatusAsync(externalTransactionId, default)
            .Returns(Result.Success(new PaymentResult(
                externalTransactionId,
                PaymentResultStatus.Succeeded,
                null)));

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert - Should dispatch ProcessExternalPaymentConfirmation
        await _mockBus.Received(1).InvokeAsync(
            Arg.Is<ProcessExternalPaymentConfirmation>(cmd =>
                cmd.ExternalTransactionId == externalTransactionId &&
                cmd.Status == "Succeeded"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcilePayments_PaymentFailedButWebhookMissed_ShouldDispatchFailure()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var externalTransactionId = "pi_test_reconcile_failed";
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction> { payment });

        // Gateway reports payment failed
        _mockGateway.GetPaymentStatusAsync(externalTransactionId, default)
            .Returns(Result.Success(new PaymentResult(
                externalTransactionId,
                PaymentResultStatus.Failed,
                "Card declined")));

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert
        await _mockBus.Received(1).InvokeAsync(
            Arg.Is<ProcessExternalPaymentConfirmation>(cmd =>
                cmd.ExternalTransactionId == externalTransactionId &&
                cmd.Status == "Failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcilePayments_PaymentStillPending_ShouldLogAndNotDispatch()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var externalTransactionId = "pi_test_still_pending";
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction> { payment });

        // Gateway still reports pending (processing not complete)
        _mockGateway.GetPaymentStatusAsync(externalTransactionId, default)
            .Returns(Result.Success(new PaymentResult(
                externalTransactionId,
                PaymentResultStatus.Pending,
                null)));

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert - Should log info but NOT dispatch (wait longer)
        await _mockBus.DidNotReceive().InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("still pending")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ReconcilePayments_PaymentRequiresAction_ShouldLogAndNotDispatch()
    {
        // Arrange - Payment requires 3D Secure authentication
        var orderId = Guid.NewGuid();
        var externalTransactionId = "pi_test_requires_action";
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction> { payment });

        _mockGateway.GetPaymentStatusAsync(externalTransactionId, default)
            .Returns(Result.Success(new PaymentResult(
                externalTransactionId,
                PaymentResultStatus.RequiresAction,
                null)));

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert - Should log but not dispatch (waiting for customer action)
        await _mockBus.DidNotReceive().InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcilePayments_PaymentWithoutExternalId_ShouldSkip()
    {
        // Arrange - Payment created but ProcessPaymentAsync failed before storing ExternalId
        var orderId = Guid.NewGuid();
        var payment = PaymentTransaction.Create(
            orderId,
            Money.Create(100m, "USD"),
            PaymentProviderDomain.Stripe,
            "idempotency_123");
        // No ExternalTransactionId set

        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction> { payment });

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert - Should log warning and not call gateway
        await _mockGateway.DidNotReceive().GetPaymentStatusAsync(Arg.Any<string>(), default);

        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("no ExternalTransactionId")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ReconcilePayments_GatewayError_ShouldLogAndContinue()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var externalTransactionId = "pi_test_gateway_error";
        var payment = CreatePendingPayment(orderId, externalTransactionId);

        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction> { payment });

        // Gateway returns error (network issue, etc)
        _mockGateway.GetPaymentStatusAsync(externalTransactionId, default)
            .Returns(Result.Failure<PaymentResult>(
                Error.Failure("Gateway.Error", "Network timeout")));

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert - Should log warning but not throw (continue with other payments)
        await _mockBus.DidNotReceive().InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());

        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to query payment status")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ReconcilePayments_MultipleStuckPayments_ShouldProcessAll()
    {
        // Arrange - 3 stuck payments
        var payment1 = CreatePendingPayment(Guid.NewGuid(), "pi_test_1");
        var payment2 = CreatePendingPayment(Guid.NewGuid(), "pi_test_2");
        var payment3 = CreatePendingPayment(Guid.NewGuid(), "pi_test_3");

        _mockRepository.GetPendingPaymentsAsync(Arg.Any<DateTime>(), default)
            .Returns(new List<PaymentTransaction> { payment1, payment2, payment3 });

        // All succeeded
        _mockGateway.GetPaymentStatusAsync(Arg.Any<string>(), default)
            .Returns(Result.Success(new PaymentResult(
                "pi_test",
                PaymentResultStatus.Succeeded,
                null)));

        var job = new PaymentReconciliationJob(_mockServiceProvider, _mockLogger);

        // Act
        var method = typeof(PaymentReconciliationJob)
            .GetMethod("ReconcilePendingPaymentsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await (Task)method!.Invoke(job, new object[] { CancellationToken.None })!;

        // Assert - Should dispatch confirmation for all 3
        await _mockBus.Received(3).InvokeAsync(
            Arg.Any<ProcessExternalPaymentConfirmation>(),
            Arg.Any<CancellationToken>());
    }

    #region Helper Methods

    private PaymentTransaction CreatePendingPayment(Guid orderId, string externalTransactionId)
    {
        var payment = PaymentTransaction.Create(
            orderId,
            Money.Create(100m, "USD"),
            PaymentProviderDomain.Stripe,
            $"idempotency_{Guid.NewGuid()}");

        payment.SetExternalTransactionId(externalTransactionId);

        // Use reflection to set CreatedAt to >10 minutes ago
        var createdAtProperty = typeof(PaymentTransaction).GetProperty("CreatedAt");
        createdAtProperty?.SetValue(payment, DateTime.UtcNow.AddMinutes(-15));

        return payment;
    }

    #endregion
}
