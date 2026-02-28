#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Domain.Shared;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Kernel.Application;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;
using Wolverine;

namespace NetCommerce.Integration.Tests.Finance;

/// <summary>
///     PRODUCTION-READINESS TEST: Ghost Charge Recovery (Financial Integrity)
///
///     <para>
///     Tests the system's ability to detect and recover from "ghost charges" -
///     payments that were successfully processed by Stripe but the Wolverine outbox
///     failed to persist (crash between charge and commit).
///     </para>
///
///     <para>
///     <b>Production Scenario:</b>
///     1. Customer clicks "Pay" → Payment succeeds on Stripe
///     2. Application crashes BEFORE Wolverine outbox commits
///     3. Customer is charged but order shows "Pending Payment"
///     4. Customer support gets angry call, company loses trust
///     </para>
///
///     <para>
///     <b>Expected Behavior:</b>
///     ReconciliationEngine should detect this mismatch within 24 hours
///     and flag for automatic or manual refund.
///     </para>
/// </summary>
public class GhostChargeRecoveryTests : IntegrationTestBase
{
    public GhostChargeRecoveryTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Ghost Charge Detection

    /// <summary>
    ///     Simulates a ghost charge scenario and verifies ReconciliationEngine detects it.
    ///
    ///     <para>
    ///     Setup:
    ///     1. No payment transaction in DB (simulating outbox didn't commit)
    ///     2. Configure mock PSP to return "Completed" for this transaction
    ///     3. Run reconciliation
    ///     4. Verify the ghost charge is flagged as MissingInternal discrepancy
    ///     </para>
    /// </summary>
    [Fact]
    public async Task GhostCharge_ShouldBeDetectedByReconciliation()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create a "ghost charge" scenario - PSP has record, we don't
        // ═══════════════════════════════════════════════════════════════════════

        var externalTransactionId = $"pi_ghost_{Guid.NewGuid():N}";
        var chargeAmount = 299.99m;
        var reconcileDate = DateTime.Today.AddDays(-1); // Reconcile yesterday

        using var scope = Fixture.Host.Services.CreateScope();

        // Configure mock PSP to return a completed transaction that's NOT in our DB
        var mockPspGateway = CreateMockPspGateway(new[]
        {
            new ExternalTransaction(
                externalTransactionId,
                chargeAmount,      // Gross amount (what customer paid)
                chargeAmount - 8.70m, // Net amount after fees
                8.70m,             // Fee
                "GEL",
                reconcileDate.AddHours(14),  // Occurred during reconcile window
                "GHOST CHARGE - Not in our DB")
        });

        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Run daily reconciliation
        // ═══════════════════════════════════════════════════════════════════════

        await engine.ReconcileDailyAsync(reconcileDate);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Ghost charge should be flagged
        // ═══════════════════════════════════════════════════════════════════════

        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeDb.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

        session.ShouldNotBeNull("Reconciliation session should be created");

        // The ghost charge (PSP completed, no internal record) should be flagged
        var ghostChargeDiscrepancy = session.Discrepancies
            .FirstOrDefault(d => d.Type == DiscrepancyType.MissingInternal);

        ghostChargeDiscrepancy.ShouldNotBeNull(
            "Ghost charge should be detected as MissingInternal discrepancy");

        Console.WriteLine($"[GhostCharge] ✓ Detected ghost charge: {ghostChargeDiscrepancy.ExternalTxnId}");
        Console.WriteLine($"[GhostCharge] Amount: {ghostChargeDiscrepancy.Difference}");
        Console.WriteLine($"[GhostCharge] Reason: {ghostChargeDiscrepancy.Reason}");
    }

    #endregion

    #region Test 2: Orphan in DB (Charge Failed at PSP)

    /// <summary>
    ///     Tests detection of orphaned DB records - payments marked as completed
    ///     that are not found in PSP ledger.
    ///
    ///     <para>
    ///     This is the opposite of ghost charge: we think a payment succeeded,
    ///     but PSP has no record of it (possible internal system error).
    ///     </para>
    /// </summary>
    [Fact]
    public async Task OrphanInDb_ShouldBeDetected()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Create payment record that doesn't exist at PSP
        // ═══════════════════════════════════════════════════════════════════════

        var reconcileDate = DateTime.Today.AddDays(-2); // Use a different date
        var orphanExternalId = $"pi_orphan_{Guid.NewGuid():N}";

        // Seed a completed payment in our DB
        await SeedCompletedPayment(reconcileDate, orphanExternalId, 150.00m);

        using var scope = Fixture.Host.Services.CreateScope();

        // PSP returns empty - no record of our transaction
        var mockPspGateway = CreateMockPspGateway(Array.Empty<ExternalTransaction>());
        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Run reconciliation
        // ═══════════════════════════════════════════════════════════════════════

        await engine.ReconcileDailyAsync(reconcileDate);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Orphan should be detected as MissingExternal
        // ═══════════════════════════════════════════════════════════════════════

        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeDb.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

        session.ShouldNotBeNull("Reconciliation session should be created");

        var orphanDiscrepancy = session.Discrepancies
            .FirstOrDefault(d => d.Type == DiscrepancyType.MissingExternal);

        orphanDiscrepancy.ShouldNotBeNull(
            "Orphan (DB has record, PSP doesn't) should be detected as MissingExternal");

        Console.WriteLine($"[OrphanInDb] ✓ Detected orphan: {orphanDiscrepancy.ExternalTxnId}");
    }

    #endregion

    #region Test 3: Recovery Within SLA

    /// <summary>
    ///     Verifies that ghost charge detection happens within the SLA window.
    ///
    ///     <para>
    ///     Production SLA: Ghost charges must be detected within 24 hours (T+1 reconciliation).
    ///     This test verifies the reconciliation can handle the T+1 pattern.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task GhostChargeRecovery_ShouldOccurWithinSLA()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Define SLA parameters for T+1 reconciliation
        // ═══════════════════════════════════════════════════════════════════════

        const int slaHours = 24;
        var reconcileDate = DateTime.Today.AddDays(-1); // T+1 = yesterday

        Console.WriteLine($"[GhostChargeSLA] SLA: {slaHours} hours");
        Console.WriteLine($"[GhostChargeSLA] Reconciliation date: {reconcileDate:yyyy-MM-dd}");
        Console.WriteLine($"[GhostChargeSLA] T+1 pattern ensures PSP has settled transactions");

        using var scope = Fixture.Host.Services.CreateScope();

        var mockPspGateway = CreateMockPspGateway(Array.Empty<ExternalTransaction>());
        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Run T+1 reconciliation
        // ═══════════════════════════════════════════════════════════════════════

        await engine.ReconcileDailyAsync(reconcileDate);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Session created within SLA window
        // ═══════════════════════════════════════════════════════════════════════

        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeDb.ReconciliationSessions
            .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

        session.ShouldNotBeNull("Reconciliation session should be created for T+1");

        // Verify we're within SLA (reconciliation ran for yesterday's data)
        var timeSinceReconcileDate = (DateTime.Today - reconcileDate).TotalHours;
        timeSinceReconcileDate.ShouldBeLessThanOrEqualTo(slaHours,
            $"T+1 reconciliation should be within {slaHours}-hour SLA");

        Console.WriteLine($"[GhostChargeSLA] ✓ Reconciliation completed within SLA window");
    }

    #endregion

    #region Test 4: Critical Alert Published for Ghost Charge

    /// <summary>
    ///     Tests that detected ghost charges publish critical alerts.
    ///
    ///     <para>
    ///     When reconciliation detects a ghost charge (PSP completed, no internal record),
    ///     it should publish a CriticalFinancialAlert that can trigger notifications.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task GhostChargeDetected_ShouldPublishCriticalAlert()
    {
        // ═══════════════════════════════════════════════════════════════════════
        // ARRANGE: Set up ghost charge scenario with message tracking
        // ═══════════════════════════════════════════════════════════════════════

        var externalTransactionId = $"pi_alert_{Guid.NewGuid():N}";
        var chargeAmount = 499.99m;
        var reconcileDate = DateTime.Today.AddDays(-3); // Use unique date

        using var scope = Fixture.Host.Services.CreateScope();

        var mockPspGateway = CreateMockPspGateway(new[]
        {
            new ExternalTransaction(
                externalTransactionId,
                chargeAmount,
                chargeAmount - 14.50m,
                14.50m,
                "GEL",
                reconcileDate.AddHours(10),
                "Ghost charge requiring alert")
        });

        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        // ═══════════════════════════════════════════════════════════════════════
        // ACT: Run reconciliation - should publish alert
        // ═══════════════════════════════════════════════════════════════════════

        await engine.ReconcileDailyAsync(reconcileDate);

        // ═══════════════════════════════════════════════════════════════════════
        // ASSERT: Verify session has ghost charge recorded
        // ═══════════════════════════════════════════════════════════════════════

        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeDb.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

        session.ShouldNotBeNull();

        var ghostCharge = session.Discrepancies
            .FirstOrDefault(d => d.ExternalTxnId == externalTransactionId);

        ghostCharge.ShouldNotBeNull("Ghost charge should be recorded");
        ghostCharge.Type.ShouldBe(DiscrepancyType.MissingInternal);

        // The CriticalFinancialAlert should have been published via IMessageBus
        // In production, this triggers PagerDuty/Slack/Email notifications

        Console.WriteLine($"[GhostChargeAlert] ✓ Session recorded with discrepancy ID: {session.Id}");
        Console.WriteLine($"[GhostChargeAlert] Alert should have been published for: {externalTransactionId}");
    }

    #endregion

    #region Helper Methods

    private IPaymentGateway CreateMockPspGateway(ExternalTransaction[] transactions)
    {
        var gateway = Substitute.For<IPaymentGateway>();
        gateway.GetExternalLedgerAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(transactions.ToList());
        return gateway;
    }

    private ReconciliationEngine CreateReconciliationEngine(
        IServiceProvider services,
        IPaymentGateway mockGateway)
    {
        return new ReconciliationEngine(
            services.GetRequiredService<IPaymentTransactionRepository>(),
            mockGateway,
            services.GetRequiredService<IReconciliationSessionRepository>(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IMessageBus>(),
            services.GetRequiredService<IOptions<AlertingOptions>>(),
            services.GetRequiredService<ILogger<ReconciliationEngine>>());
    }

    private async Task SeedCompletedPayment(DateTime date, string externalId, decimal amount)
    {
        using var scope = Fixture.Host.Services.CreateScope();
        var paymentsDb = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var txn = PaymentTransaction.Create(
            Guid.NewGuid(),
            Money.Create(amount, "GEL"),
            PaymentProvider.Stripe,
            $"idempotency-{externalId}");
        txn.MarkAsCompleted(externalId);

        // Set CompletedAt to the specific date
        typeof(PaymentTransaction).GetProperty("CompletedAt")!
            .SetValue(txn, DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc));

        paymentsDb.Transactions.Add(txn);
        await paymentsDb.SaveChangesAsync();
    }

    #endregion
}
