using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Finance.Domain.Reconciliation;

/// <summary>
///     Reconciliation Session aggregate for financial reconciliation.
///     Compares internal payment ledger against external PSP data.
///     2025 Triple-Lock Reconciliation: Internal vs External vs Audit Logs.
/// </summary>
public sealed class ReconciliationSession : AggregateRoot<Guid>
{
    private ReconciliationSession()
    {
        // EF Core constructor
    }

    public DateTime CalculatedForDate { get; private set; }
    public ReconciliationStatus Status { get; private set; }
    public decimal TotalInternalAmount { get; private set; }
    public decimal TotalExternalAmount { get; private set; }
    public List<Discrepancy> Discrepancies { get; private set; } = new();
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? Notes { get; private set; }

    public static ReconciliationSession Create(DateTime date)
    {
        return new ReconciliationSession
        {
            Id = Guid.NewGuid(),
            CalculatedForDate = date.Date,
            Status = ReconciliationStatus.Started,
            StartedAt = DateTime.UtcNow,
            Discrepancies = new List<Discrepancy>()
        };
    }

    public void AddDiscrepancy(Discrepancy discrepancy)
    {
        Discrepancies.Add(discrepancy);

        // Auto-escalate to Mismatched if any discrepancy found
        if (Status == ReconciliationStatus.Started)
        {
            Status = ReconciliationStatus.Mismatched;
        }
    }

    public void SetTotals(decimal internalTotal, decimal externalTotal)
    {
        TotalInternalAmount = Math.Round(internalTotal, 2);
        TotalExternalAmount = Math.Round(externalTotal, 2);
    }

    public void MarkAsCompleted()
    {
        Status = Discrepancies.Count == 0 ? ReconciliationStatus.Matched : ReconciliationStatus.Mismatched;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string reason)
    {
        Status = ReconciliationStatus.Failed;
        Notes = reason;
        CompletedAt = DateTime.UtcNow;
    }

    public void AddNote(string note)
    {
        Notes = string.IsNullOrEmpty(Notes) ? note : $"{Notes}; {note}";
    }
}

/// <summary>
///     Value object representing a financial discrepancy.
/// </summary>
public record Discrepancy(
    string ExternalTxnId,
    DiscrepancyType Type,
    decimal Difference,
    string Reason,
    DateTime DetectedAt)
{
    public Discrepancy(string externalTxnId, DiscrepancyType type, decimal difference, string reason)
        : this(externalTxnId, type, difference, reason, DateTime.UtcNow)
    {
    }
}

public enum ReconciliationStatus
{
    Started = 0,
    Matched = 1,
    Mismatched = 2,
    Failed = 3
}

public enum DiscrepancyType
{
    /// <summary>
    /// PSP has a charge, but no internal payment record exists (Ghost Charge - CRITICAL)
    /// </summary>
    MissingInternal = 0,

    /// <summary>
    /// Internal system shows payment, but PSP has no record
    /// </summary>
    MissingExternal = 1,

    /// <summary>
    /// Amounts don't match (currency rounding, tax calculation differences)
    /// </summary>
    AmountMismatch = 2,

    /// <summary>
    /// PSP fees are higher than expected
    /// </summary>
    FeeMismatch = 3
}
