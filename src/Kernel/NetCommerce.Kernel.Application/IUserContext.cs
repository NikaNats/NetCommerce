#nullable enable
namespace NetCommerce.Kernel.Application;

/// <summary>
///     User context service for extracting claims from JWT tokens or other authentication mechanisms.
///     Provides WHO is performing the action for audit logging and authorization.
/// </summary>
public interface IUserContext
{
    /// <summary>
    ///     The unique identifier of the currently authenticated user.
    ///     Examples: "user_abc123", "admin_xyz789", "system@netcommerce.com"
    /// </summary>
    string UserId { get; }

    /// <summary>
    ///     The role(s) of the user at the time of the action.
    ///     Examples: "Admin", "Vendor", "Customer", "System"
    ///     CRITICAL: This should reflect the role at action time, not current role.
    /// </summary>
    string Role { get; }

    /// <summary>
    ///     Optional: The IP address from which the request originated.
    ///     Useful for security audits and geographic compliance.
    /// </summary>
    string? IpAddress { get; }

    /// <summary>
    ///     Optional: The user agent (browser/API client) making the request.
    /// </summary>
    string? UserAgent { get; }

    /// <summary>
    ///     Whether a user is currently authenticated.
    ///     For background jobs, this might be false and UserId would be "system".
    /// </summary>
    bool IsAuthenticated { get; }
}
