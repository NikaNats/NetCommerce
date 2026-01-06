using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Application;
using NSubstitute;
using Wolverine;

namespace NetCommerce.Domain.Tests.Finance;

/// <summary>
///     Unit tests for ReconciliationEngine.
///     Tests the core double-entry ledger verification logic.
/// </summary>
public class ReconciliationEngineTests
{
    private readonly IPaymentTransactionRepository _internalRepo;
    private readonly IPaymentGateway _pspGateway;
    private readonly IReconciliationSessionRepository _sessionRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMessageBus _bus;
    private readonly ILogger<ReconciliationEngine> _logger;
    private readonly ReconciliationEngine _engine;

    public ReconciliationEngineTests()
    {
        _internalRepo = Substitute.For<IPaymentTransactionRepository>();
        _pspGateway = Substitute.For<IPaymentGateway>();
        _sessionRepo = Substitute.For<IReconciliationSessionRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _bus = Substitute.For<IMessageBus>();
        _logger = Substitute.For<ILogger<ReconciliationEngine>>();

        _engine = new ReconciliationEngine(
            _internalRepo,
            _pspGateway,
            _sessionRepo,
            _unitOfWork,
            _bus,
            _logger);
    }

    [Fact]
    public async Task ReconcileDailyAsync_PerfectMatch_ShouldCreateMatchedSession()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        var internalTxns = new List<PaymentTransaction>
        {
            CreatePaymentTransaction(Guid.NewGuid(), "ch_123", 99.99m), // Matches external GROSS
            CreatePaymentTransaction(Guid.NewGuid(), "ch_456", 149.99m) // Matches external GROSS
        };
        var externalTxns = new List<ExternalTransaction>
        {
            new ExternalTransaction("ch_123", 99.99m, 97.49m, 2.50m, "USD", DateTime.UtcNow, "Payment"),
            new ExternalTransaction("ch_456", 149.99m, 146.09m, 3.90m, "USD", DateTime.UtcNow, "Payment")
        };

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Matched &&
                s.Discrepancies.Count == 0 &&
                s.TotalInternalAmount == 249.98m), // 99.99 + 149.99
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_GhostCharge_ShouldDetectMissingInternal()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        var internalTxns = new List<PaymentTransaction>
        {
            CreatePaymentTransaction(Guid.NewGuid(), "ch_123", 99.99m) // Matches external GROSS
        };
        var externalTxns = new List<ExternalTransaction>
        {
            new ExternalTransaction("ch_123", 99.99m, 97.49m, 2.50m, "USD", DateTime.UtcNow, "Payment"),
            new ExternalTransaction("ch_ghost", 500.00m, 485.50m, 14.50m, "USD", DateTime.UtcNow, "GHOST CHARGE")
        };

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Mismatched &&
                s.Discrepancies.Count == 1),
            Arg.Any<CancellationToken>());

        // Should publish critical alert
        await _bus.Received(1).PublishAsync(
            Arg.Is<CriticalFinancialAlert>(a =>
                a.ExternalTransactionId == "ch_ghost" &&
                a.Amount == 500.00m));
    }

    [Fact]
    public async Task ReconcileDailyAsync_MissingExternal_ShouldDetectSystemError()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        var internalTxns = new List<PaymentTransaction>
        {
            CreatePaymentTransaction(Guid.NewGuid(), "ch_123", 99.99m),
            CreatePaymentTransaction(Guid.NewGuid(), "ch_missing", 75.00m)
        };
        var externalTxns = new List<ExternalTransaction>
        {
            new ExternalTransaction("ch_123", 99.99m, 97.49m, 2.50m, "USD", DateTime.UtcNow, "Payment")
        };

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Mismatched &&
                s.Discrepancies.Any(d =>
                    d.Type == DiscrepancyType.MissingExternal &&
                    d.ExternalTxnId == "ch_missing")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_AmountMismatch_ShouldDetectDifference()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        var internalTxns = new List<PaymentTransaction>
        {
            CreatePaymentTransaction(Guid.NewGuid(), "ch_123", 100.00m)
        };
        var externalTxns = new List<ExternalTransaction>
        {
            new ExternalTransaction("ch_123", 99.50m, 97.26m, 2.24m, "USD", DateTime.UtcNow, "Payment") // GROSS = 99.50, difference = 100.00 - 99.50 = 0.50
        };

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Discrepancies.Any(d =>
                    d.Type == DiscrepancyType.AmountMismatch &&
                    d.ExternalTxnId == "ch_123" &&
                    Math.Abs(d.Difference - 0.50m) < 0.01m)), // 100.00 - 99.50 = 0.50
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_SmallRoundingDifference_ShouldNotFlagAsDiscrepancy()
    {
        // Arrange (0.009 difference - within 1 cent tolerance)
        var date = DateTime.Today.AddDays(-1);
        var internalTxns = new List<PaymentTransaction>
        {
            CreatePaymentTransaction(Guid.NewGuid(), "ch_123", 100.000m)
        };
        var externalTxns = new List<ExternalTransaction>
        {
            new ExternalTransaction("ch_123", 100.009m, 99.991m, 0.018m, "USD", DateTime.UtcNow, "Payment") // Net = 99.991, difference = 100.000 - 99.991 = 0.009
        };

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert - should be matched with no discrepancies due to small rounding difference
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Matched &&
                s.Discrepancies.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_NoInternalTransactions_ShouldStillReconcile()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        var internalTxns = new List<PaymentTransaction>();
        var externalTxns = new List<ExternalTransaction>();

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Matched &&
                s.TotalInternalAmount == 0 &&
                s.TotalExternalAmount == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_PSPFailure_ShouldMarkSessionAsFailed()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(new List<PaymentTransaction>());
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ExternalTransaction>>(new HttpRequestException("API connection failed")));

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_TransactionMissingExternalId_ShouldFlagAsDiscrepancy()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        var txnWithoutExternalId = PaymentTransaction.Create(
            Guid.NewGuid(),
            new Money(100m, "USD"),
            PaymentProvider.Stripe,
            "idempotency-missing-external");
        // Complete it without external ID
        txnWithoutExternalId.MarkAsCompleted(null);

        var internalTxns = new List<PaymentTransaction> { txnWithoutExternalId };
        var externalTxns = new List<ExternalTransaction>();

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Discrepancies.Any(d =>
                    d.Type == DiscrepancyType.MissingExternal &&
                    d.Reason.Contains("no external ID"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_MultipleGhostCharges_ShouldPublishMultipleAlerts()
    {
        // Arrange
        var date = DateTime.Today.AddDays(-1);
        var internalTxns = new List<PaymentTransaction>();
        var externalTxns = new List<ExternalTransaction>
        {
            new ExternalTransaction("ch_ghost_1", 100.00m, 97.00m, 3.00m, "USD", DateTime.UtcNow, "Ghost 1"),
            new ExternalTransaction("ch_ghost_2", 200.00m, 194.00m, 6.00m, "USD", DateTime.UtcNow, "Ghost 2"),
            new ExternalTransaction("ch_ghost_3", 50.00m, 48.55m, 1.45m, "USD", DateTime.UtcNow, "Ghost 3")
        };

        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(internalTxns);
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns(externalTxns);

        // Act
        await _engine.ReconcileDailyAsync(date);

        // Assert
        await _bus.Received(3).PublishAsync(Arg.Any<CriticalFinancialAlert>());
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Discrepancies.Count == 3 &&
                s.Discrepancies.All(d => d.Type == DiscrepancyType.MissingInternal)),
            Arg.Any<CancellationToken>());
    }

    private PaymentTransaction CreatePaymentTransaction(Guid orderId, string externalId, decimal amount)
    {
        var txn = PaymentTransaction.Create(
            orderId,
            new Money(amount, "USD"),
            PaymentProvider.Stripe,
            $"idempotency-{orderId}");
        txn.MarkAsCompleted(externalId);
        return txn;
    }
}
