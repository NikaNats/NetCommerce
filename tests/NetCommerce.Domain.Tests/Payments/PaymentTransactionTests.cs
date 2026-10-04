using NetCommerce.Domain.Shared;
using NetCommerce.Payments.Domain.Transactions;
using Shouldly;

namespace NetCommerce.Domain.Tests.Payments;

/// <summary>
///     Guard tests for the <see cref="PaymentTransaction"/> state machine
///     (distilled DDD: Aggregates protect meaningful state transitions).
/// </summary>
/// <remarks>
/// <para>
///     Money must never be resurrected or double-settled by a late or duplicate
///     message: completing a refunded payment, failing a completed one, or
///     refunding a never-captured one are illegal transitions, not silent
///     overwrites. Callers (webhook handler, saga handlers) pre-check
///     idempotency and treat these throws as poison input, never as outcomes.
/// </para>
/// </remarks>
public sealed class PaymentTransactionTests
{
    private static PaymentTransaction CreatePending()
    {
        return PaymentTransaction.Create(
            Guid.NewGuid(),
            Money.Create(100m, "USD"),
            PaymentProvider.Stripe,
            $"idempotency_{Guid.NewGuid():N}");
    }

    [Fact]
    public void MarkAsCompleted_FromPending_ShouldComplete()
    {
        var payment = CreatePending();

        payment.MarkAsCompleted("pi_123");

        payment.Status.ShouldBe(PaymentStatus.Completed);
        payment.ExternalTransactionId.ShouldBe("pi_123");
    }

    [Theory]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void MarkAsCompleted_FromTerminal_ShouldThrow(PaymentStatus terminal)
    {
        var payment = DriveTo(terminal);

        Should.Throw<InvalidOperationException>(() => payment.MarkAsCompleted("pi_late"));
        payment.Status.ShouldBe(terminal);
    }

    [Fact]
    public void MarkAsFailed_FromPending_ShouldFail()
    {
        var payment = CreatePending();

        payment.MarkAsFailed("declined");

        payment.Status.ShouldBe(PaymentStatus.Failed);
    }

    [Theory]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void MarkAsFailed_FromTerminal_ShouldThrow(PaymentStatus terminal)
    {
        var payment = DriveTo(terminal);

        Should.Throw<InvalidOperationException>(() => payment.MarkAsFailed("late news"));
        payment.Status.ShouldBe(terminal);
    }

    [Fact]
    public void MarkAsRefunded_FromCompleted_ShouldRefund()
    {
        var payment = DriveTo(PaymentStatus.Completed);

        payment.MarkAsRefunded("re_123");

        payment.Status.ShouldBe(PaymentStatus.Refunded);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void MarkAsRefunded_WithoutCapture_ShouldThrow(PaymentStatus status)
    {
        var payment = DriveTo(status);

        Should.Throw<InvalidOperationException>(() => payment.MarkAsRefunded("re_123"));
        payment.Status.ShouldBe(status);
    }

    [Fact]
    public void SetExternalTransactionId_OnTerminal_ShouldThrow()
    {
        var payment = DriveTo(PaymentStatus.Completed);

        Should.Throw<InvalidOperationException>(() => payment.SetExternalTransactionId("pi_other"));
        payment.ExternalTransactionId.ShouldBe("pi_123");
    }

    private static PaymentTransaction DriveTo(PaymentStatus status)
    {
        var payment = CreatePending();
        payment.SetExternalTransactionId("pi_123");

        switch (status)
        {
            case PaymentStatus.Pending:
                break;
            case PaymentStatus.Completed:
                payment.MarkAsCompleted("pi_123");
                break;
            case PaymentStatus.Failed:
                payment.MarkAsFailed("declined");
                break;
            case PaymentStatus.Refunded:
                payment.MarkAsCompleted("pi_123");
                payment.MarkAsRefunded("re_123");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported test target state.");
        }

        payment.Status.ShouldBe(status);
        return payment;
    }
}
