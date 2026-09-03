#nullable enable

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Finance.Domain.Webhooks;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Payments.Domain.Transactions;
using Shouldly;
using Wolverine;

namespace NetCommerce.Integration.Tests.Payments;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "WebhookStorm")]
public sealed class WebhookConcurrentRetryStormTests : IntegrationTestBase
{
    public WebhookConcurrentRetryStormTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task WebhookRetryStorm_50ConcurrentDeliveriesOfSameEvent_MustProcessExactlyOnce()
    {
        const int concurrentRetries = 50;
        var orderId = Guid.NewGuid();
        var sharedStripeEventId = $"evt_storm_{Guid.NewGuid():N}";
        var externalTransactionId = $"pi_storm_{Guid.NewGuid():N}";

        // 1. Seed base payment transaction
        await using (var paymentsDb = Fixture.CreatePaymentsDbContext())
        {
            var transaction = PaymentTransaction.Create(
                orderId,
                Money.Create(399.00m, "GEL"),
                PaymentProvider.Stripe,
                $"idemp_storm_{orderId:N}");

            transaction.SetExternalTransactionId(externalTransactionId);
            paymentsDb.Transactions.Add(transaction);
            await paymentsDb.SaveChangesAsync();
        }

        // 2. Resolve a scope factory: each concurrent delivery gets its own
        // scoped IWebhookEventStore/DbContext, mirroring production where each
        // HTTP webhook request runs in an isolated DI scope.
        var scopeFactory = Services.GetRequiredService<IServiceScopeFactory>();

        var results = new ConcurrentBag<bool>();
        var duplicateClaims = 0;
        var successfulClaims = 0;

        // 3. ACT: Launch 50 parallel claim attempts for the exact same event ID
        var tasks = Enumerable.Range(0, concurrentRetries).Select(async i =>
        {
            using var taskScope = scopeFactory.CreateScope();
            var taskStore = taskScope.ServiceProvider.GetRequiredService<IWebhookEventStore>();
            var taskBus = taskScope.ServiceProvider.GetRequiredService<IMessageBus>();

            var claimed = await taskStore.TryClaimEventAsync(
                sharedStripeEventId,
                "payment_intent.succeeded",
                externalTransactionId,
                CancellationToken.None);

            if (claimed)
            {
                Interlocked.Increment(ref successfulClaims);
                // Simulate message dispatch
                await taskBus.InvokeAsync(new ProcessExternalPaymentConfirmation(
                    externalTransactionId,
                    "Succeeded",
                    sharedStripeEventId
                ));
                await taskStore.MarkProcessedAsync(sharedStripeEventId);
                results.Add(true);
            }
            else
            {
                Interlocked.Increment(ref duplicateClaims);
                results.Add(false);
            }
        });

        await Task.WhenAll(tasks);

        // 4. ASSERT: Exactly-Once Processing Invariants
        successfulClaims.ShouldBe(1,
            $"CONCURRENCY FAILURE: Event {sharedStripeEventId} was claimed {successfulClaims} times concurrently! Expected exactly 1.");

        duplicateClaims.ShouldBe(concurrentRetries - 1,
            $"Deduplication failed to reject {concurrentRetries - 1} duplicate webhook delivery attempts.");

        // 5. Verify database records
        await using (var financeDb = Fixture.CreateFinanceDbContext())
        {
            var recordedEvents = await financeDb.ProcessedWebhookEvents
                .AsNoTracking()
                .Where(e => e.StripeEventId == sharedStripeEventId)
                .ToListAsync();

            recordedEvents.Count.ShouldBe(1, "PostgreSQL idempotency table contains duplicate rows for the same Stripe Event ID.");
            recordedEvents[0].Status.ShouldBe(WebhookProcessingStatus.Processed);
        }

        // 6. Verify Payment Transaction was marked Completed once without conflict
        await using (var paymentsDb = Fixture.CreatePaymentsDbContext())
        {
            var finalPayment = await paymentsDb.Transactions
                .AsNoTracking()
                .FirstAsync(t => t.OrderId == orderId);

            finalPayment.Status.ShouldBe(PaymentStatus.Completed);
            finalPayment.CompletedAt.ShouldNotBeNull();
        }
    }
}
