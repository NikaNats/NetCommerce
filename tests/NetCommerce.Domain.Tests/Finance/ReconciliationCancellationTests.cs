using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Finance.Application.Services;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Application;
using NSubstitute;
using Wolverine;

namespace NetCommerce.Domain.Tests.Finance;

/// <summary>
///     Tests for the shutdown vs failure distinction in <see cref="ReconciliationEngine"/>.
///     A PSP timeout (cancellation NOT requested) is a reconcilable failure; a SIGTERM-driven
///     cancellation must still persist the session so the next T+1 run reprocesses the date
///     instead of assuming success. Previously both paths shared one catch.
/// </summary>
public class ReconciliationCancellationTests
{
    private readonly IPaymentTransactionReadService _internalRepo;
    private readonly IPaymentGateway _pspGateway;
    private readonly IReconciliationSessionRepository _sessionRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMessageBus _bus;
    private readonly ReconciliationEngine _engine;

    public ReconciliationCancellationTests()
    {
        _internalRepo = Substitute.For<IPaymentTransactionReadService>();
        _pspGateway = Substitute.For<IPaymentGateway>();
        _sessionRepo = Substitute.For<IReconciliationSessionRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _bus = Substitute.For<IMessageBus>();

        _engine = new ReconciliationEngine(
            _internalRepo,
            _pspGateway,
            _sessionRepo,
            _unitOfWork,
            _bus,
            Options.Create(new AlertingOptions { DiscrepancyAlertThreshold = 100m }),
            Substitute.For<ILogger<ReconciliationEngine>>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_WhenShutdownCancelled_ShouldPersistFailedSessionWithoutRethrow()
    {
        // Arrange: SIGTERM arrives mid-run; the PSP call observes the ambient token.
        var date = DateTime.Today.AddDays(-1);
        using var cts = new CancellationTokenSource();
        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(new List<PaymentTransactionSummary>());
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ExternalTransaction>>>(_ => throw new OperationCanceledException(cts.Token));
        cts.Cancel();

        // Act: must NOT throw (the host is stopping; crashing loses the audit trail).
        var exception = await Record.ExceptionAsync(() => _engine.ReconcileDailyAsync(date, cts.Token));

        // Assert: session persisted as failed so tomorrow's run picks this date up again.
        exception.ShouldBeNull();
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Failed &&
                s.Notes != null && s.Notes.Contains("Cancelled during shutdown")),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconcileDailyAsync_WhenPspTimesOut_ShouldMarkFailedWithTimeoutReason()
    {
        // Arrange: the PSP client times out on its own (live token) — a reconcilable failure,
        // NOT a shutdown. The `when` filter must not swallow it as a cancellation.
        var date = DateTime.Today.AddDays(-1);
        using var cts = new CancellationTokenSource();
        _internalRepo.GetCompletedByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(new List<PaymentTransactionSummary>());
        _pspGateway.GetExternalLedgerAsync(date, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ExternalTransaction>>>(_ => throw new TaskCanceledException("PSP timeout"));

        // Act
        await _engine.ReconcileDailyAsync(date, cts.Token);

        // Assert: failed session carries the timeout reason (not the shutdown message).
        await _sessionRepo.Received(1).AddAsync(
            Arg.Is<ReconciliationSession>(s =>
                s.Status == ReconciliationStatus.Failed &&
                s.Notes != null && s.Notes.Contains("PSP timeout")),
            Arg.Any<CancellationToken>());
    }
}
