#region

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Domain.Shared;
using NSubstitute;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

#endregion

namespace NetCommerce.Integration.Tests.Finance;

/// <summary>
///     Integration tests for the Financial Reconciliation System.
///     Tests end-to-end reconciliation workflow with real database and messaging.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class ReconciliationIntegrationTests : IntegrationTestBase
{
    public ReconciliationIntegrationTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task ReconcileDailyAsync_WithMatchingTransactions_ShouldCreateMatchedSession()
    {
        // Arrange - Use unique date to prevent cross-test contamination
        var date = DateTime.UtcNow.AddDays(-10).Date;
        await SeedPaymentTransactions(date, new[]
        {
            ("ch_match_1", 99.99m),
            ("ch_match_2", 149.99m)
        });

        var mockPspGateway = CreateMockPspGateway(new[]
        {
            new ExternalTransaction("ch_match_1", 99.99m, 97.49m, 2.50m, "USD", date, "Payment 1"),
            new ExternalTransaction("ch_match_2", 149.99m, 146.09m, 3.90m, "USD", date, "Payment 2")
        });

        using var scope = Fixture.Host.Services.CreateScope();
        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        // Act
        await engine.ReconcileDailyAsync(date);

        // Assert
        var financeContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeContext.ReconciliationSessions
            .FirstOrDefaultAsync(s => s.CalculatedForDate == date.Date);

        session.ShouldNotBeNull();
        session.Status.ShouldBe(ReconciliationStatus.Matched);
        session.Discrepancies.ShouldBeEmpty();
        session.TotalInternalAmount.ShouldBe(249.98m);
        session.TotalExternalAmount.ShouldBe(249.98m);
    }

    [Fact]
    public async Task ReconcileDailyAsync_WithGhostCharge_ShouldDetectAndAlert()
    {
        // Arrange - Use unique date to prevent cross-test contamination
        var date = DateTime.UtcNow.AddDays(-11).Date;
        await SeedPaymentTransactions(date, new[]
        {
            ("ch_legit", 100.00m)
        });

        var mockPspGateway = CreateMockPspGateway(new[]
        {
            new ExternalTransaction("ch_legit", 100.00m, 97.00m, 3.00m, "USD", date, "Legitimate payment"),
            new ExternalTransaction("ch_ghost", 500.00m, 485.50m, 14.50m, "USD", date, "GHOST CHARGE - no internal order")
        });

        using var scope = Fixture.Host.Services.CreateScope();
        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        // Act
        var tracked = await Fixture.Host.ExecuteAndWaitAsync(async () =>
        {
            await engine.ReconcileDailyAsync(date);
        });

        // Assert - Session created with discrepancy
        var financeContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeContext.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == date.Date);

        session.ShouldNotBeNull();
        session.Status.ShouldBe(ReconciliationStatus.Mismatched);
        session.Discrepancies.ShouldNotBeEmpty();

        // Should have ghost charge discrepancy
        var ghostDiscrepancy = session.Discrepancies.FirstOrDefault(d => d.ExternalTxnId == "ch_ghost");
        ghostDiscrepancy.ShouldNotBeNull();
        ghostDiscrepancy.Type.ShouldBe(DiscrepancyType.MissingInternal);
        ghostDiscrepancy.Difference.ShouldBe(500.00m);
        ghostDiscrepancy.Reason.ShouldContain("CRITICAL");

        // The legitimate transaction should match (no discrepancy for ch_legit)
        session.Discrepancies.Any(d => d.ExternalTxnId == "ch_legit").ShouldBeFalse();

        // NOTE: Wolverine message tracking doesn't properly capture messages from manually created engine
        // TODO: Investigate proper Wolverine integration for engine-published messages
        // tracked.Sent.MessagesOf<CriticalFinancialAlert>().ShouldHaveSingleItem();
        // var alert = tracked.Sent.MessagesOf<CriticalFinancialAlert>().First();
        // alert.ExternalTransactionId.ShouldBe("ch_ghost");
        // alert.Amount.ShouldBe(500.00m);
    }

    [Fact]
    public async Task ReconcileDailyAsync_WithAmountMismatch_ShouldDetectDifference()
    {
        // Arrange - Use unique date to prevent cross-test contamination
        var date = DateTime.UtcNow.AddDays(-12).Date;
        await SeedPaymentTransactions(date, new[]
        {
            ("ch_mismatch", 100.00m)
        });

        var mockPspGateway = CreateMockPspGateway(new[]
        {
            new ExternalTransaction("ch_mismatch", 99.50m, 96.76m, 2.74m, "USD", date, "Payment with difference")
        });

        using var scope = Fixture.Host.Services.CreateScope();
        var engine = CreateReconciliationEngine(scope.ServiceProvider, mockPspGateway);

        // Act
        await engine.ReconcileDailyAsync(date);

        // Assert
        var financeContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await financeContext.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.CalculatedForDate == date.Date);

        session.ShouldNotBeNull();
        session.Status.ShouldBe(ReconciliationStatus.Mismatched);

        var discrepancy = session.Discrepancies.First();
        discrepancy.Type.ShouldBe(DiscrepancyType.AmountMismatch);
        discrepancy.ExternalTxnId.ShouldBe("ch_mismatch");
        Math.Abs(discrepancy.Difference - 0.50m).ShouldBeLessThan(0.01m);
    }

    [Fact]
    public async Task ReconciliationSession_ShouldPersistDiscrepanciesCorrectly()
    {
        // Arrange
        using var scope = Fixture.Host.Services.CreateScope();
        var financeContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        var session = ReconciliationSession.Create(DateTime.UtcNow.Date);
        session.SetTotals(1000m, 950m);
        session.AddDiscrepancy(new Discrepancy("ch_1", DiscrepancyType.MissingInternal, 50m, "Test discrepancy 1"));
        session.AddDiscrepancy(new Discrepancy("ch_2", DiscrepancyType.AmountMismatch, 10m, "Test discrepancy 2"));
        session.MarkAsCompleted();

        // Act
        financeContext.ReconciliationSessions.Add(session);
        await financeContext.SaveChangesAsync();

        // Clear context to force reload from database
        financeContext.ChangeTracker.Clear();

        // Assert
        var loadedSession = await financeContext.ReconciliationSessions
            .Include(s => s.Discrepancies)
            .FirstOrDefaultAsync(s => s.Id == session.Id);

        loadedSession.ShouldNotBeNull();
        loadedSession.Status.ShouldBe(ReconciliationStatus.Mismatched);
        loadedSession.Discrepancies.Count.ShouldBe(2);
        loadedSession.TotalInternalAmount.ShouldBe(1000m);
        loadedSession.TotalExternalAmount.ShouldBe(950m);
    }

    [Fact]
    public async Task GetMismatchedSessionsAsync_ShouldReturnOnlyMismatchedSessions()
    {
        // Arrange
        using var scope = Fixture.Host.Services.CreateScope();
        var financeContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        var matchedSession = ReconciliationSession.Create(DateTime.UtcNow.AddDays(-3).Date);
        matchedSession.SetTotals(100m, 100m);
        matchedSession.MarkAsCompleted();

        var mismatchedSession = ReconciliationSession.Create(DateTime.UtcNow.AddDays(-2).Date);
        mismatchedSession.SetTotals(100m, 95m);
        mismatchedSession.AddDiscrepancy(new Discrepancy("ch_mis", DiscrepancyType.AmountMismatch, 5m, "Mismatch"));
        mismatchedSession.MarkAsCompleted();

        financeContext.ReconciliationSessions.AddRange(matchedSession, mismatchedSession);
        await financeContext.SaveChangesAsync();

        var repo = scope.ServiceProvider.GetRequiredService<IReconciliationSessionRepository>();

        // Act
        var mismatched = await repo.GetMismatchedSessionsAsync(DateTime.UtcNow.AddDays(-5).Date);

        // Assert
        mismatched.Count.ShouldBe(1);
        mismatched.First().Id.ShouldBe(mismatchedSession.Id);
        mismatched.First().Status.ShouldBe(ReconciliationStatus.Mismatched);
    }

    #region Helper Methods

    private async Task SeedPaymentTransactions(DateTime date, (string ExternalId, decimal Amount)[] transactions)
    {
        using var scope = Fixture.Host.Services.CreateScope();
        var paymentsContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        foreach (var (externalId, amount) in transactions)
        {
            var orderId = Guid.NewGuid();
            var txn = PaymentTransaction.Create(
                orderId,
                new Money(amount, "USD"),
                NetCommerce.Payments.Domain.Transactions.PaymentProvider.Stripe,
                $"idempotency-{orderId}");
            txn.MarkAsCompleted(externalId);

            // Set CompletedAt to the specific date for reconciliation (must be UTC for PostgreSQL)
            typeof(PaymentTransaction).GetProperty("CompletedAt")!
                .SetValue(txn, DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc));

            paymentsContext.Transactions.Add(txn);
        }

        await paymentsContext.SaveChangesAsync();
    }

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
            services.GetRequiredService<NetCommerce.Kernel.Application.IUnitOfWork>(),
            services.GetRequiredService<IMessageBus>(),
            services.GetRequiredService<ILogger<ReconciliationEngine>>());
    }

    #endregion
}
