#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Infrastructure.Handlers;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Domain.Stock;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Payments.Domain.Transactions;
using NSubstitute;
using Shouldly;
using Wolverine;

namespace NetCommerce.Integration.Tests.Chaos;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "DisasterRecovery")]
public sealed class PitrAndSagaResyncDrillTests : IntegrationTestBase
{
    public PitrAndSagaResyncDrillTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task PitrRestoration_WithMissingTransactions_MustDetectGhostChargesAndReconcileCleanly()
    {
        var reconcileDate = DateTime.UtcNow.Date.AddDays(-1);
        var productId = Guid.NewGuid();
        var sku = $"SKU-PITR-{Guid.NewGuid():N}";
        const int initialStock = 100;

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 1: Baseline Pre-Disaster State (T-30 Minutes)
        // ═══════════════════════════════════════════════════════════════════════
        await using (var inventoryDb = Fixture.CreateInventoryDbContext())
        {
            var stock = Stock.Create(productId, sku, initialStock);
            inventoryDb.Stocks.Add(stock);
            await inventoryDb.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 2: Disaster Simulation — 3 Transactions Succeeded on Stripe,
        // but Database Rollback/PITR Wipes Them From PostgreSQL
        // ═══════════════════════════════════════════════════════════════════════
        var ghostTxn1 = new ExternalTransaction($"pi_pitr_ghost_1_{Guid.NewGuid():N}", 150.00m, 145.50m, 4.50m, "GEL", reconcileDate.AddHours(14), "Customer A (Lost in PITR)");
        var ghostTxn2 = new ExternalTransaction($"pi_pitr_ghost_2_{Guid.NewGuid():N}", 300.00m, 291.00m, 9.00m, "GEL", reconcileDate.AddHours(15), "Customer B (Lost in PITR)");
        var validTxn = new ExternalTransaction($"pi_pitr_valid_3_{Guid.NewGuid():N}", 50.00m, 48.50m, 1.50m, "GEL", reconcileDate.AddHours(12), "Customer C (Persisted Pre-PITR)");

        // Seed ONLY the valid transaction into PostgreSQL (simulating PITR restoration point)
        await using (var paymentsDb = Fixture.CreatePaymentsDbContext())
        {
            var validPayment = PaymentTransaction.Create(
                Guid.NewGuid(),
                Money.Create(50.00m, "GEL"),
                PaymentProvider.Stripe,
                $"idemp_valid_{Guid.NewGuid():N}");

            validPayment.MarkAsCompleted(validTxn.Id);
            typeof(PaymentTransaction).GetProperty("CompletedAt")!
                .SetValue(validPayment, DateTime.SpecifyKind(reconcileDate.AddHours(12), DateTimeKind.Utc));

            paymentsDb.Transactions.Add(validPayment);
            await paymentsDb.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 3: Run T+1 Reconciliation Against Real Stripe Ledger
        // ═══════════════════════════════════════════════════════════════════════
        var mockPspGateway = Substitute.For<IPaymentGateway>();
        mockPspGateway.GetExternalLedgerAsync(reconcileDate, Arg.Any<CancellationToken>())
            .Returns(new List<ExternalTransaction> { ghostTxn1, ghostTxn2, validTxn });

        mockPspGateway.RefundTransactionAsync(ghostTxn2.Id, ghostTxn2.Amount, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns($"re_pitr_refund_{Guid.NewGuid():N}");

        using var scope = Fixture.Host.Services.CreateScope();
        var engine = new ReconciliationEngine(
            scope.ServiceProvider.GetRequiredService<IPaymentTransactionReadService>(),
            mockPspGateway,
            scope.ServiceProvider.GetRequiredService<IReconciliationSessionRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<IMessageBus>(),
            scope.ServiceProvider.GetRequiredService<IOptions<AlertingOptions>>(),
            scope.ServiceProvider.GetRequiredService<ILogger<ReconciliationEngine>>());

        await engine.ReconcileDailyAsync(reconcileDate);

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 4: Assert Discrepancy Detection & Alert Invariants
        // ═══════════════════════════════════════════════════════════════════════
        await using (var financeDb = Fixture.CreateFinanceDbContext())
        {
            var session = await financeDb.ReconciliationSessions
                .Include(s => s.Discrepancies)
                .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

            session.ShouldNotBeNull();
            session.Status.ShouldBe(ReconciliationStatus.Mismatched);
            session.Discrepancies.Count.ShouldBe(2);

            session.Discrepancies.ShouldContain(d => d.Type == DiscrepancyType.MissingInternal && d.ExternalTxnId == ghostTxn1.Id);
            session.Discrepancies.ShouldContain(d => d.Type == DiscrepancyType.MissingInternal && d.ExternalTxnId == ghostTxn2.Id);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 5: Execute Admin Recovery Workflows
        // 1. Create Shadow Order for Ghost 1 (Customer wants goods)
        // 2. Refund Ghost 2 (Customer wants money back)
        // ═══════════════════════════════════════════════════════════════════════
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // 5a. Create Shadow Order via Wolverine cross-module dispatch
        var shadowOrderCmd = new CreateShadowOrderCommand(
            ExternalTransactionId: ghostTxn1.Id,
            Amount: ghostTxn1.Amount,
            Currency: ghostTxn1.Currency,
            ResolvedBy: "admin@netcommerce.internal",
            Reason: "PITR Database Disaster Recovery");

        var shadowResult = await bus.InvokeAsync<Result<Guid>>(shadowOrderCmd);
        shadowResult.IsSuccess.ShouldBeTrue();

        // 5b. Resolve Discrepancy 2 via Refund.
        // NOTE: DiscrepancyResolutionHandler is invoked directly (rather than via
        // bus dispatch) because the Finance IPaymentGateway is intentionally NOT
        // registered in the shared test host — the suite injects an NSubstitute
        // mock PSP gateway so the refund call is verifiable. Handler semantics
        // (refund reason format, ghost-only guard) are identical either way.
        Guid sessionId;
        await using (var financeDb = Fixture.CreateFinanceDbContext())
        {
            sessionId = (await financeDb.ReconciliationSessions.FirstAsync(s => s.CalculatedForDate == reconcileDate)).Id;
        }

        var resolveDiscrepancyCmd = new ResolveDiscrepancyCommand(
            SessionId: sessionId,
            ExternalTxnId: ghostTxn2.Id,
            Action: DiscrepancyResolutionAction.RefundGhostCharge,
            Reason: "Customer requested refund after database desync",
            ResolvedBy: "admin@netcommerce.internal");

        await DiscrepancyResolutionHandler.Handle(
            resolveDiscrepancyCmd,
            scope.ServiceProvider.GetRequiredService<IReconciliationSessionRepository>(),
            mockPspGateway,
            bus,
            NullLogger.Instance,
            CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 6: Assert Post-Recovery Invariants
        // ═══════════════════════════════════════════════════════════════════════

        // 1. Shadow Order exists in Ordering schema with Paid status
        await using (var orderingDb = Fixture.CreateOrderingDbContext())
        {
            var shadowOrder = await orderingDb.Orders.FirstOrDefaultAsync(o => o.Id == shadowResult.Value);
            shadowOrder.ShouldNotBeNull();
            shadowOrder.IsShadowOrder.ShouldBeTrue();
            shadowOrder.Status.ShouldBe(OrderStatus.Paid);
            shadowOrder.SourceDiscrepancyTxnId.ShouldBe(ghostTxn1.Id);
        }

        // 2. Refund Gateway was invoked exactly once for Ghost 2
        await mockPspGateway.Received(1).RefundTransactionAsync(
            ghostTxn2.Id,
            ghostTxn2.Amount,
            Arg.Is<string>(r => r.Contains("Ghost charge resolution")),
            Arg.Any<CancellationToken>());

        // 3. Physical Inventory stock must remain untouched by reconciliation
        await using (var inventoryDb = Fixture.CreateInventoryDbContext())
        {
            var finalStock = await inventoryDb.Stocks.FirstAsync(s => s.ProductId == productId);
            finalStock.Quantity.ShouldBe(initialStock, "Reconciliation shadow creation must never corrupt physical inventory counts.");
            finalStock.ReservedQuantity.ShouldBe(0);
        }
    }
}
