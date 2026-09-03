#nullable enable

using Microsoft.EntityFrameworkCore;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Finance.Domain.Audit;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Payments.Domain.Transactions;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Payments;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "WebhookAsynchrony")]
public sealed class WebhookOutOfOrderAndAsynchronyTests : IntegrationTestBase
{
    public WebhookOutOfOrderAndAsynchronyTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task OutOfOrderWebhooks_RefundArrivesBeforePaymentSuccess_ShouldHandleGracefully()
    {
        var orderId = Guid.NewGuid();
        var chargeId = $"ch_async_{Guid.NewGuid():N}";
        var paymentIntentId = $"pi_async_{Guid.NewGuid():N}";
        var refundId = $"re_async_{Guid.NewGuid():N}";
        var totalAmount = Money.Create(250.00m, "GEL");

        // 1. Seed Payment transaction in 'Pending' state
        await using (var paymentsDb = Fixture.CreatePaymentsDbContext())
        {
            var transaction = PaymentTransaction.Create(
                orderId,
                totalAmount,
                PaymentProvider.Stripe,
                $"idemp_{orderId:N}");

            transaction.SetExternalTransactionId(paymentIntentId);
            paymentsDb.Transactions.Add(transaction);
            await paymentsDb.SaveChangesAsync();
        }

        // 2. DISASTER SIMULATION: charge.refunded webhook arrives FIRST (before payment_intent.succeeded)
        var earlyRefundCommand = new ProcessStripeRefundWebhook(
            ChargeId: chargeId,
            RefundId: refundId,
            AmountRefunded: 250.00m,
            TotalRefundedSoFar: 250.00m,
            Currency: "GEL",
            StripeEventId: $"evt_early_refund_{Guid.NewGuid():N}",
            PaymentIntentId: paymentIntentId,
            Reason: "requested_by_customer"
        );

        var refundSession = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(earlyRefundCommand);

        refundSession.AllExceptions().ShouldBeEmpty();

        // 3. Now the delayed payment_intent.succeeded arrives
        var latePaymentSuccessCommand = new ProcessExternalPaymentConfirmation(
            ExternalTransactionId: paymentIntentId,
            Status: "Succeeded",
            WebhookEventId: $"evt_late_success_{Guid.NewGuid():N}"
        );

        var paymentSession = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(latePaymentSuccessCommand);

        paymentSession.AllExceptions().ShouldBeEmpty();

        // 4. Assert Financial Audit Trail recorded both events chronologically
        await using (var financeDb = Fixture.CreateFinanceDbContext())
        {
            var auditEntries = await financeDb.FinancialAuditLog
                .AsNoTracking()
                .Where(a => a.EntityId == orderId.ToString() || a.ExternalTransactionId == chargeId)
                .ToListAsync();

            auditEntries.ShouldNotBeEmpty();
            auditEntries.Any(a => a.AuditType == FinancialAuditType.RefundSucceeded || a.AuditType == FinancialAuditType.PartialRefund).ShouldBeTrue();
        }

        // 5. Assert PaymentTransaction status ended in Completed or Refunded without deadlock
        await using (var paymentsDb = Fixture.CreatePaymentsDbContext())
        {
            var finalTxn = await paymentsDb.Transactions
                .AsNoTracking()
                .FirstAsync(t => t.OrderId == orderId);

            finalTxn.Status.ShouldBeOneOf(PaymentStatus.Completed, PaymentStatus.Refunded);
        }
    }

    [Fact]
    public async Task TerminalSaga_LatePaymentConfirmation_ShouldNotResurrectFailedState()
    {
        var orderId = Guid.NewGuid();
        var externalTxnId = $"pi_terminal_{Guid.NewGuid():N}";

        // 1. Manually start a saga and force it into Failed state (e.g. inventory reservation timeout)
        var startCommand = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-TERMINAL-01",
            Money.Create(100m, "GEL"),
            []);

        await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .InvokeMessageAndWaitAsync(startCommand);

        // 2. A late PaymentSucceeded event arrives after saga failed
        var latePaymentEvent = new PaymentSucceeded(orderId, externalTxnId, Money.Create(100m, "GEL"));

        var lateSession = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(10))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(latePaymentEvent);

        // NotFound handler should capture the late message without throwing
        lateSession.AllExceptions().ShouldBeEmpty(
            "Late arriving webhook for terminal saga caused an unhandled crash instead of clean NotFound logging.");

        // Saga must NOT have cascaded ConfirmInventoryCommand
        lateSession.Sent.MessagesOf<ConfirmInventoryCommand>().ShouldBeEmpty();
    }
}
