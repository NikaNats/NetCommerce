#nullable enable
namespace NetCommerce.Kernel.Application;

/// <summary>
///     Service to resolve the current Tenant ID from the execution context
///     (HTTP Header, JWT Claim, Subdomain, or Background Job configuration).
/// </summary>
public interface ITenantContext
{
    /// <summary>
    ///     The current Tenant ID.
    ///     If null, the system may decide to throw or return no data.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    ///     Returns true if a tenant context is currently active.
    /// </summary>
    bool HasTenant { get; }
}
