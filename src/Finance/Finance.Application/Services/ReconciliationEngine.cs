using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Finance.Domain.Gateways;
using NetCommerce.Finance.Domain.Reconciliation;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Domain.Shared.Events;
using Wolverine;

namespace NetCommerce.Finance.Application.Services;

/// <summary>
///     Configuration options for financial alerting.
///     Bound from configuration section "Finance:Alerting".
/// </summary>
public sealed class AlertingOptions
{
    public const string SectionName = "Finance:Alerting";

    /// <summary>
    ///     PagerDuty Events API routing key. Set to enable PagerDuty alerts.
    /// </summary>
    public string? PagerDutyRoutingKey { get; set; }

    /// <summary>
    ///     Threshold in currency amount above which discrepancies trigger alerts.
    ///     Default: $100.00
    /// </summary>
    public decimal DiscrepancyAlertThreshold { get; set; } = 100.00m;

    /// <summary>
    ///     Whether to send email alerts in addition to PagerDuty.
    /// </summary>
    public bool SendEmailAlerts { get; set; } = true;

    /// <summary>
    ///     Email address for finance team alerts.
    /// </summary>
    public string FinanceAlertEmail { get; set; } = "finance-alerts@company.com";
}

/// <summary>
///     Core reconciliation engine implementing Double-Entry Ledger Verification.
///     Compares Internal Reality (our DB) vs External Reality (PSP).
///     2025 Triple-Lock: Internal Ledger vs PSP API vs Transaction Logs.
/// </summary>
public sealed class ReconciliationEngine
{
    private readonly IPaymentTransactionReadService _internalRepo;
    private readonly IPaymentGateway _pspGateway;
    private readonly IReconciliationSessionRepository _sessionRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMessageBus _bus;
    private readonly AlertingOptions _alertingOptions;
    private readonly ILogger<ReconciliationEngine> _logger;

    public ReconciliationEngine(
        IPaymentTransactionReadService internalRepo,
        IPaymentGateway pspGateway,
        IReconciliationSessionRepository sessionRepo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IOptions<AlertingOptions> alertingOptions,
        ILogger<ReconciliationEngine> logger)
    {
        _internalRepo = internalRepo;
        _pspGateway = pspGateway;
        _sessionRepo = sessionRepo;
        _unitOfWork = unitOfWork;
        _bus = bus;
        _alertingOptions = alertingOptions.Value;
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
        IReadOnlyList<PaymentTransactionSummary> internalTxns,
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
        IReadOnlyList<PaymentTransactionSummary> internalTxns,
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
        // 1. Always alert for ghost charges (money taken without order)
        var ghostCharges = session.Discrepancies
            .Where(d => d.Type == DiscrepancyType.MissingInternal)
            .ToList();

        foreach (var ghostCharge in ghostCharges)
        {
            _logger.LogCritical(
                "Publishing GHOST CHARGE alert: {ExternalId}, Amount={Amount}",
                ghostCharge.ExternalTxnId, ghostCharge.Difference);

            await _bus.PublishAsync(new CriticalFinancialAlert(
                ghostCharge.ExternalTxnId,
                ghostCharge.Difference,
                ghostCharge.Reason));
        }

        // 2. Alert for amount mismatches above threshold
        var significantMismatches = session.Discrepancies
            .Where(d => d.Type == DiscrepancyType.AmountMismatch &&
                        Math.Abs(d.Difference) >= _alertingOptions.DiscrepancyAlertThreshold)
            .ToList();

        foreach (var mismatch in significantMismatches)
        {
            _logger.LogWarning(
                "Publishing amount mismatch alert: {ExternalId}, Difference={Difference} (threshold: {Threshold})",
                mismatch.ExternalTxnId, mismatch.Difference, _alertingOptions.DiscrepancyAlertThreshold);

            await _bus.PublishAsync(new CriticalFinancialAlert(
                mismatch.ExternalTxnId,
                mismatch.Difference,
                $"Amount mismatch exceeds ${_alertingOptions.DiscrepancyAlertThreshold:N2} threshold: {mismatch.Reason}"));
        }

        // 3. Alert for missing external records above threshold (possible failed captures)
        var missingExternal = session.Discrepancies
            .Where(d => d.Type == DiscrepancyType.MissingExternal &&
                        Math.Abs(d.Difference) >= _alertingOptions.DiscrepancyAlertThreshold)
            .ToList();

        foreach (var missing in missingExternal)
        {
            _logger.LogWarning(
                "Publishing missing PSP record alert: {ExternalId}, Amount={Amount}",
                missing.ExternalTxnId, missing.Difference);

            await _bus.PublishAsync(new CriticalFinancialAlert(
                missing.ExternalTxnId,
                missing.Difference,
                $"Transaction marked completed but not in PSP ledger: {missing.Reason}"));
        }

        // 4. Log summary metrics
        var alertCount = ghostCharges.Count + significantMismatches.Count + missingExternal.Count;
        if (alertCount > 0)
        {
            _logger.LogWarning(
                "Reconciliation completed with {AlertCount} alerts: {GhostCharges} ghost charges, " +
                "{AmountMismatches} amount mismatches, {MissingExternal} missing PSP records",
                alertCount, ghostCharges.Count, significantMismatches.Count, missingExternal.Count);
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
