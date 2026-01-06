#nullable enable
namespace NetCommerce.Kernel.Compliance.Audit;

/// <summary>
///     Marker interface for commands that require audit logging.
///     Only commands implementing this interface are automatically audited.
/// </summary>
public interface IAuditableCommand
{
    /// <summary>
    ///     The module/bounded context where this command belongs.
    ///     Examples: "Ordering", "Catalog", "Payments", "Inventory"
    /// </summary>
    string Module { get; }

    /// <summary>
    ///     The ID of the business entity being modified.
    ///     This enables timeline views: "Show me all audit entries for Order #12345"
    /// </summary>
    string GetResourceId();
}
