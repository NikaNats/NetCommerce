namespace NetCommerce.Finance.Application.Commands;

/// <summary>
///     Command to resolve a financial discrepancy manually.
///     Used by admin UI for human-in-the-loop corrections.
/// </summary>
public record ResolveDiscrepancyCommand(
    Guid SessionId,
    string ExternalTxnId,
    DiscrepancyResolutionAction Action,
    string Reason,
    string ResolvedBy);

public enum DiscrepancyResolutionAction
{
    /// <summary>
    /// Create a shadow order to account for the ghost charge
    /// </summary>
    CreateShadowOrder = 0,

    /// <summary>
    /// Issue immediate refund via PSP
    /// </summary>
    RefundGhostCharge = 1,

    /// <summary>
    /// Mark discrepancy as accepted (with audit trail)
    /// </summary>
    AcceptDiscrepancy = 2,

    /// <summary>
    /// Flag for further investigation
    /// </summary>
    InvestigateFurther = 3
}
