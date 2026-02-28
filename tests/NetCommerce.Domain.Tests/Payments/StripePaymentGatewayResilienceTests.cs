using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Kernel.Stripe;
using NetCommerce.Payments.Application.Gateways;
using NetCommerce.Payments.Infrastructure.Gateways;
using NSubstitute;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Shouldly;
using Stripe;

namespace NetCommerce.Domain.Tests.Payments;

/// <summary>
///     Unit tests for StripePaymentGateway Polly resilience pipeline integration.
///
///     <para>
///     Strategy: use <see cref="CircuitBreakerStateProvider.Isolate"/> to force the circuit
///     breaker into the Open state, which makes the pipeline throw
///     <see cref="IsolatedCircuitException"/> (inherits from <see cref="BrokenCircuitException"/>)
///     without making any actual Stripe API calls.
///     This validates the fast-fail guard in the gateway without needing live Stripe credentials.
///     </para>
/// </summary>
public class StripePaymentGatewayResilienceTests
{
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly IOptions<StripeOptions> _options;

    public StripePaymentGatewayResilienceTests()
    {
        _logger = Substitute.For<ILogger<StripePaymentGateway>>();
        _options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_placeholder",
            PublishableKey = "pk_test_placeholder",
            WebhookSecret = "whsec_placeholder"
        });
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Builds a ResiliencePipeline with a circuit breaker whose state can be
    ///     externally controlled via the returned <see cref="CircuitBreakerManualControl"/>.
    /// </summary>
    private static (ResiliencePipeline Pipeline, CircuitBreakerManualControl ManualControl) BuildControllablePipeline()
    {
        var manualControl = new CircuitBreakerManualControl();

        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ManualControl = manualControl,
                ShouldHandle = new PredicateBuilder().Handle<StripeException>(),
                MinimumThroughput = 2,
                FailureRatio = 1.0,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(300)
            })
            .Build();

        return (pipeline, manualControl);
    }

    // ─── ProcessPaymentAsync Tests ───────────────────────────────────────────────

    [Fact]
    public async Task ProcessPaymentAsync_WhenCircuitIsIsolated_ShouldReturnCircuitOpenFailure()
    {
        // Arrange
        var (pipeline, manualControl) = BuildControllablePipeline();
        await manualControl.IsolateAsync(); // Force the circuit into open state

        var gateway = new StripePaymentGateway(_options, _logger, pipeline);

        var request = new PaymentRequest(
            OrderId: Guid.NewGuid(),
            Amount: Money.Create(199.99m, "USD"),
            PaymentMethodToken: "pm_card_visa",
            IdempotencyKey: Guid.NewGuid().ToString());

        // Act
        var result = await gateway.ProcessPaymentAsync(request, CancellationToken.None);

        // Assert — circuit open must fast-fail without touching Stripe
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Payment.CircuitOpen");
    }

    [Fact]
    public async Task ProcessPaymentAsync_WithPassthroughPipeline_ShouldAttemptStripeCall()
    {
        // Arrange — ResiliencePipeline.Empty passes all calls through without any strategy
        // The call will fail because SecretKey is a placeholder, but the pipeline wrapping code
        // is proven to work (it doesn't short-circuit before reaching Stripe SDK).
        var gateway = new StripePaymentGateway(_options, _logger, ResiliencePipeline.Empty);

        var request = new PaymentRequest(
            OrderId: Guid.NewGuid(),
            Amount: Money.Create(10m, "USD"),
            PaymentMethodToken: "pm_invalid",
            IdempotencyKey: Guid.NewGuid().ToString());

        // Act — StripeException expected (invalid key), caught by gateway and wrapped as Success(Failed)
        var result = await gateway.ProcessPaymentAsync(request, CancellationToken.None);

        // Assert — Result.Success (domain failure), NOT Result.Failure (infrastructure error)
        // Stripe returns an authentication_error for a fake key, which maps to a StripeException
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(PaymentResultStatus.Failed);
    }

    // ─── GetPaymentStatusAsync Tests ────────────────────────────────────────────

    [Fact]
    public async Task GetPaymentStatusAsync_WhenCircuitIsIsolated_ShouldReturnCircuitOpenFailure()
    {
        // Arrange
        var (pipeline, manualControl) = BuildControllablePipeline();
        await manualControl.IsolateAsync();

        var gateway = new StripePaymentGateway(_options, _logger, pipeline);

        // Act
        var result = await gateway.GetPaymentStatusAsync("pi_fake_txn_id", CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Payment.CircuitOpen");
    }

    // ─── ProcessRefundAsync Tests ────────────────────────────────────────────────

    [Fact]
    public async Task ProcessRefundAsync_WhenCircuitIsIsolated_ShouldReturnCircuitOpenFailure()
    {
        // Arrange
        var (pipeline, manualControl) = BuildControllablePipeline();
        await manualControl.IsolateAsync();

        var gateway = new StripePaymentGateway(_options, _logger, pipeline);

        var request = new RefundRequest(
            OriginalTransactionId: "pi_fake_txn_id",
            Amount: Money.Create(50m, "USD"),
            Reason: "Inventory confirmation failed");

        // Act
        var result = await gateway.ProcessRefundAsync(request, CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Refund.CircuitOpen");
    }

    // ─── Retry Strategy Configuration Tests ─────────────────────────────────────

    [Fact]
    public async Task RetryPipeline_ShouldNotRetryCardDeclinedErrors()
    {
        // Arrange — Build the same pipeline as PaymentsModule registers
        var retryCount = 0;
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<StripeException>(ex => ex.IsTransient()),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero, // Instant for tests
                OnRetry = args =>
                {
                    retryCount++;
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        // Card-declined is NOT transient; IsTransient() returns false for card_declined code
        var cardDeclinedException = new StripeException(
            System.Net.HttpStatusCode.PaymentRequired,
            new StripeError { Code = "card_declined", Message = "Your card was declined." },
            "card_declined");

        // Act — The pipeline executes the action; StripeException is thrown but ShouldHandle
        // returns false for card_declined so no retry occurs.
        await Should.ThrowAsync<StripeException>(async () =>
            await pipeline.ExecuteAsync(async _ =>
            {
                throw cardDeclinedException;
#pragma warning disable CS0162 // Unreachable code after throw
                return await Task.FromResult(0);
#pragma warning restore CS0162
            }));

        // Assert — no retry was triggered for a non-transient card decline
        retryCount.ShouldBe(0);
    }
}
