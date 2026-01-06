using Microsoft.Extensions.Logging;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Payments.Domain.Transactions;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Domain.Shared.Events;
using Wolverine;

namespace NetCommerce.Finance.Application.Services;

/// <summary>
///     Core reconciliation engine implementing Double-Entry Ledger Verification.
///     Compares Internal Reality (our DB) vs External Reality (PSP).
///     2025 Triple-Lock: Internal Ledger vs PSP API vs Transaction Logs.
/// </summary>
public sealed class ReconciliationEngine
{
    private readonly IPaymentTransactionRepository _internalRepo;
    private readonly IPaymentGateway _pspGateway;
    private readonly IReconciliationSessionRepository _sessionRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMessageBus _bus;
    private readonly ILogger<ReconciliationEngine> _logger;

    public ReconciliationEngine(
        IPaymentTransactionRepository internalRepo,
        IPaymentGateway pspGateway,
        IReconciliationSessionRepository sessionRepo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<ReconciliationEngine> logger)
    {
        _internalRepo = internalRepo;
        _pspGateway = pspGateway;
        _sessionRepo = sessionRepo;
        _unitOfWork = unitOfWork;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    ///     Perform daily reconciliation for the specified date.
    ///     T+1 Rule: Reconcile yesterday to ensure PSP settlement is complete.
    /// </summary>
    public async Task ReconcileDailyAsync(DateTime date, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting daily reconciliation for {Date}", date.ToShortDateString());

        var session = ReconciliationSession.Create(date);

        try
        {
            // 1. Fetch internal COMPLETED transactions for the date
            var internalTxns = await _internalRepo.GetCompletedByDateAsync(date, ct);
            _logger.LogInformation("Found {Count} internal completed transactions", internalTxns.Count);

            // 2. Fetch external PSP ledger for the date (The External Truth)
            var externalTxns = await _pspGateway.GetExternalLedgerAsync(date, ct);
            _logger.LogInformation("Found {Count} external PSP transactions", externalTxns.Count);

            // 3. Set totals for audit trail - compare GROSS amounts
            var internalTotal = internalTxns.Sum(t => t.Amount.Amount);
            var externalTotal = externalTxns.Sum(t => t.Amount);
            session.SetTotals(internalTotal, externalTotal);

            // 4. Perform Left-Outer-Join: Check our records against PSP
            await PerformInternalToExternalComparisonAsync(internalTxns, externalTxns, session, ct);

            // 5. Perform Right-Outer-Join: Detect GHOST CHARGES
            await PerformExternalToInternalComparisonAsync(internalTxns, externalTxns, session, ct);

            // 6. Mark session as completed
            session.MarkAsCompleted();

            _logger.LogInformation(
                "Reconciliation completed. Status: {Status}, Discrepancies: {Count}",
                session.Status, session.Discrepancies.Count);

            // 7. Publish critical alerts for ghost charges
            await PublishCriticalAlertsAsync(session, ct);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciliation failed for {Date}", date);
            session.MarkAsFailed(ex.Message);
        }

        // 8. Save session regardless of outcome
        await _sessionRepo.AddAsync(session, ct);
        // When called directly (not through Wolverine handler), save changes explicitly
        // When called through [Transactional] handler, Wolverine will handle this
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task PerformInternalToExternalComparisonAsync(
        IReadOnlyList<PaymentTransaction> internalTxns,
        IReadOnlyList<ExternalTransaction> externalTxns,
        ReconciliationSession session,
        CancellationToken ct)
    {
        foreach (var internalTxn in internalTxns)
        {
            if (string.IsNullOrEmpty(internalTxn.ExternalTransactionId))
            {
                session.AddDiscrepancy(new Discrepancy(
                    internalTxn.Id.ToString(),
                    DiscrepancyType.MissingExternal,
                    internalTxn.Amount.Amount,
                    "Internal transaction has no external ID - possible system error"));
                continue;
            }

            var matchingExternal = externalTxns.FirstOrDefault(
                x => x.Id == internalTxn.ExternalTransactionId);

            if (matchingExternal == null)
            {
                session.AddDiscrepancy(new Discrepancy(
                    internalTxn.ExternalTransactionId!,
                    DiscrepancyType.MissingExternal,
                    internalTxn.Amount.Amount,
                    $"Transaction marked 'Completed' internally but not found in PSP ledger"));
                continue;
            }

            // Check amount mismatch (account for rounding differences)
            // Compare GROSS amounts - internal payment amount vs external PSP gross amount
            var amountDiff = Math.Abs(internalTxn.Amount.Amount - matchingExternal.Amount);
            if (amountDiff > 0.01m) // Allow 1 cent tolerance
            {
                session.AddDiscrepancy(new Discrepancy(
                    internalTxn.ExternalTransactionId!,
                    DiscrepancyType.AmountMismatch,
                    internalTxn.Amount.Amount - matchingExternal.Amount,
                    $"Internal: {internalTxn.Amount.Amount}, External Gross: {matchingExternal.Amount}"));
            }
        }
    }

    private async Task PerformExternalToInternalComparisonAsync(
        IReadOnlyList<PaymentTransaction> internalTxns,
        IReadOnlyList<ExternalTransaction> externalTxns,
        ReconciliationSession session,
        CancellationToken ct)
    {
        foreach (var external in externalTxns)
        {
            var hasMatchingInternal = internalTxns.Any(
                x => x.ExternalTransactionId == external.Id);

            if (!hasMatchingInternal)
            {
                // CRITICAL: Ghost Charge detected!
                session.AddDiscrepancy(new Discrepancy(
                    external.Id,
                    DiscrepancyType.MissingInternal,
                    external.Amount,
                    $"CRITICAL: Customer charged {external.Amount} {external.Currency} in PSP but no order exists in our system!"));

                _logger.LogCritical(
                    "GHOST CHARGE DETECTED: ExternalTxnId={ExternalId}, Amount={Amount} {Currency}",
                    external.Id, external.Amount, external.Currency);
            }
        }
    }

    private async Task PublishCriticalAlertsAsync(ReconciliationSession session, CancellationToken ct)
    {
        var ghostCharges = session.Discrepancies
            .Where(d => d.Type == DiscrepancyType.MissingInternal)
            .ToList();

        foreach (var ghostCharge in ghostCharges)
        {
            await _bus.PublishAsync(new CriticalFinancialAlert(
                ghostCharge.ExternalTxnId,
                ghostCharge.Difference,
                ghostCharge.Reason));
        }
    }
}

/// <summary>
///     Domain event for critical financial alerts (ghost charges, etc.)
/// </summary>
public record CriticalFinancialAlert(
    string ExternalTransactionId,
    decimal Amount,
    string Reason) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
