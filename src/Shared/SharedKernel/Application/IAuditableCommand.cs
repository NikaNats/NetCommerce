namespace NetCommerce.SharedKernel.Application;

/// <summary>
///     2025 Elite Pattern: Marker interface for commands that require audit logging.
///     Why a marker interface?
///     - Explicit intent: Only commands implementing this are audited (no noise)
///     - Type safety: Wolverine middleware can filter by this interface
///     - Self-documenting: Looking at a command, you immediately know if it's audited
///     Examples of auditable commands:
///     - CancelOrderCommand (financial impact)
///     - ChangeProductPriceCommand (regulatory compliance)
///     - RefundPaymentCommand (legal record)
///     Examples of NON-auditable commands:
///     - AddToCartCommand (too much noise, no legal requirement)
///     - SearchProductsQuery (read-only operations)
/// </summary>
public interface IAuditableCommand
{
    /// <summary>
    ///     The module/bounded context where this command belongs.
    ///     Examples: "Ordering", "Catalog", "Payments", "Inventory"
    ///     This is used for filtering and security permissions.
    /// </summary>
    string Module { get; }

    /// <summary>
    ///     The ID of the business entity being modified.
    ///     This enables timeline views: "Show me all audit entries for Order #12345"
    /// </summary>
    string GetResourceId();
}
