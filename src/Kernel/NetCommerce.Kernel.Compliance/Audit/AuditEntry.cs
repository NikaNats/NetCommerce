#nullable enable
namespace NetCommerce.Kernel.Compliance.Audit;

/// <summary>
///     Immutable Business Event Store for Legal Compliance.
///     This is NOT a technical log (that's Seq/OpenTelemetry).
///     This is a LEGAL RECORD that must persist for years and be tamper-proof.
/// </summary>
public sealed record AuditEntry
{
    /// <summary>
    ///     Unique identifier for this audit entry.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    ///     UTC timestamp when the action occurred.
    ///     This is the PRIMARY sorting key for timeline views.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    ///     WHO did it? The user ID from JWT claims or API key.
    ///     Examples: "user_123", "admin_456", "system@company.com"
    /// </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    ///     The role of the user at the time of the action.
    ///     Examples: "Admin", "Vendor", "Customer", "System"
    ///     CRITICAL: Store the role at the time of action (not current role).
    /// </summary>
    public string UserRole { get; init; } = string.Empty;

    /// <summary>
    ///     WHAT happened? Semantic business action, not technical implementation.
    ///     Examples: "Order.Cancelled", "Price.Changed", "Refund.Issued"
    ///     Format: "{Module}.{Action}" for easy filtering.
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>
    ///     The ID of the business entity affected.
    ///     Examples: OrderId, ProductId, UserId
    ///     This enables "show me all audit entries for Order #12345"
    /// </summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>
    ///     The module/bounded context where the action occurred.
    ///     Examples: "Ordering", "Catalog", "Payments", "Inventory"
    /// </summary>
    public string Module { get; init; } = string.Empty;

    /// <summary>
    ///     WHY did it happen? The business context as JSON.
    ///     Examples:
    ///     - Order cancellation: { "Reason": "Fraud suspect", "PreviousStatus": "Paid" }
    ///     - Price change: { "OldPrice": 100, "NewPrice": 80, "Reason": "Promotion" }
    /// </summary>
    public string Context { get; init; } = string.Empty;

    /// <summary>
    ///     Link to technical observability stack (Seq, OpenTelemetry, Application Insights).
    ///     Enables jumping from business audit → technical trace.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    ///     Optional: IP address from which the action was performed.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    ///     Optional: User agent that performed the action.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    ///     Factory method for creating audit entries with validation.
    /// </summary>
    public static AuditEntry Create(
        string userId,
        string userRole,
        string action,
        string resourceId,
        string module,
        string context,
        string correlationId,
        string? ipAddress = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required for audit trail", nameof(userId));
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required for audit trail", nameof(action));
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("ResourceId is required for audit trail", nameof(resourceId));
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Module is required for audit trail", nameof(module));

        return new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            UserRole = userRole,
            Action = action,
            ResourceId = resourceId,
            Module = module,
            Context = context,
            CorrelationId = correlationId,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
    }
}
