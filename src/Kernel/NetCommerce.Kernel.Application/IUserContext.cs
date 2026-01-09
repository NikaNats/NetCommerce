#nullable enable
using System.Security.Claims;

namespace NetCommerce.Kernel.Application;

/// <summary>
///     High-performance, Claims-based User Context.
///     Follows .NET Principal/Identity guidelines from the official Security Guide.
/// </summary>
public interface IUserContext
{
    /// <summary>
    ///     The underlying .NET Principal. Enables standard [Authorize] and IsInRole logic.
    /// </summary>
    ClaimsPrincipal User { get; }

    /// <summary>
    ///     Strongly-typed User ID (extracted from 'sub' or 'NameIdentifier' claim).
    /// </summary>
    string UserId { get; }

    /// <summary>
    ///     Standardized Tenant ID (extracted from custom 'tid' or 'tenant_id' claim).
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    ///     Checks if the principal is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    ///     Direct access to specific claims without iterating manually.
    /// </summary>
    string? GetClaim(string claimType);

    /// <summary>
    ///     All roles associated with the user (for audit/compliance purposes).
    /// </summary>
    IEnumerable<string> Roles { get; }

    /// <summary>
    ///     Checks membership in a role using the standard IPrincipal.IsInRole logic.
    /// </summary>
    bool IsInRole(string role);
}
