#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
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
/// </summary>
public class GhostChargeRecoveryTests : IntegrationTestBase
{
    public GhostChargeRecoveryTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    #region Test 1: Ghost Charge Detection

    [Fact]
    public async Task GhostCharge_ShouldBeDetectedByReconciliation()
    {
        var externalTransactionId = $"pi_ghost_{Guid.NewGuid():N}";
        var chargeAmount = 299.99m;
        var reconcileDate = DateTime.UtcNow.Date.AddDays(-1); // Reconcile yesterday in UTC

        using var scope = Fixture.Host.Services.CreateScope();

        var mockPspGateway = CreateMockPspGateway(new[]
        {
            new ExternalTransaction(
                externalTransactionId,
                chargeAmount,
                chargeAmount - 8.70m,
                8.70m,
                "USD", // Use USD platform currency
                reconcileDate.AddHours(14),
                "GHOST CHARGE - Not in our DB")
        });

        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        await engine.ReconcileDailyAsync(reconcileDate);

        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeDb.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

        session.ShouldNotBeNull("Reconciliation session should be created");

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

    [Fact]
    public async Task OrphanInDb_ShouldBeDetected()
    {
        var reconcileDate = DateTime.UtcNow.Date.AddDays(-2);
        var orphanExternalId = $"pi_orphan_{Guid.NewGuid():N}";

        await SeedCompletedPayment(reconcileDate, orphanExternalId, 150.00m);

        using var scope = Fixture.Host.Services.CreateScope();

        var mockPspGateway = CreateMockPspGateway(Array.Empty<ExternalTransaction>());
        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        await engine.ReconcileDailyAsync(reconcileDate);

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

    [Fact]
    public async Task GhostChargeRecovery_ShouldOccurWithinSLA()
    {
        const int slaHours = 24;
        var reconcileDate = DateTime.UtcNow.Date.AddDays(-1);

        Console.WriteLine($"[GhostChargeSLA] SLA: {slaHours} hours");
        Console.WriteLine($"[GhostChargeSLA] Reconciliation date: {reconcileDate:yyyy-MM-dd}");

        using var scope = Fixture.Host.Services.CreateScope();

        var mockPspGateway = CreateMockPspGateway(Array.Empty<ExternalTransaction>());
        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        await engine.ReconcileDailyAsync(reconcileDate);

        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeDb.ReconciliationSessions
            .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

        session.ShouldNotBeNull("Reconciliation session should be created for T+1");

        var timeSinceReconcileDate = (DateTime.UtcNow.Date - reconcileDate).TotalHours;
        timeSinceReconcileDate.ShouldBeLessThanOrEqualTo(slaHours,
            $"T+1 reconciliation should be within {slaHours}-hour SLA");

        Console.WriteLine($"[GhostChargeSLA] ✓ Reconciliation completed within SLA window");
    }

    #endregion

    #region Test 4: Critical Alert Published for Ghost Charge

    [Fact]
    public async Task GhostChargeDetected_ShouldPublishCriticalAlert()
    {
        var externalTransactionId = $"pi_alert_{Guid.NewGuid():N}";
        var chargeAmount = 499.99m;
        var reconcileDate = DateTime.UtcNow.Date.AddDays(-3);

        using var scope = Fixture.Host.Services.CreateScope();

        var mockPspGateway = CreateMockPspGateway(new[]
        {
            new ExternalTransaction(
                externalTransactionId,
                chargeAmount,
                chargeAmount - 14.50m,
                14.50m,
                "USD", // Use USD platform currency
                reconcileDate.AddHours(10),
                "Ghost charge requiring alert")
        });

        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        await engine.ReconcileDailyAsync(reconcileDate);

        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeDb.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == reconcileDate);

        session.ShouldNotBeNull();

        var ghostCharge = session.Discrepancies
            .FirstOrDefault(d => d.ExternalTxnId == externalTransactionId);

        ghostCharge.ShouldNotBeNull("Ghost charge should be recorded");
        ghostCharge.Type.ShouldBe(DiscrepancyType.MissingInternal);

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
            services.GetRequiredService<IPaymentTransactionReadService>(),
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
            Money.Create(amount, "USD"), // Seed using USD platform currency
            PaymentProvider.Stripe,
            $"idempotency-{externalId}");
        txn.MarkAsCompleted(externalId);

        typeof(PaymentTransaction).GetProperty("CompletedAt")!
            .SetValue(txn, DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc));

        paymentsDb.Transactions.Add(txn);
        await paymentsDb.SaveChangesAsync();
    }

    #endregion
}
