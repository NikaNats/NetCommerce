#nullable enable

namespace NetCommerce.Kernel.Compliance.Audit;

/// <summary>
///     User context abstraction for audit purposes.
///     Implementation will be provided by the Application layer.
/// </summary>
public interface IUserContext
{
    /// <summary>
    ///     Gets the current user's ID.
    /// </summary>
    string UserId { get; }

    /// <summary>
    ///     Gets the current user's name.
    /// </summary>
    string? UserName { get; }

    /// <summary>
    ///     Gets the current user's role.
    /// </summary>
    string Role { get; }

    /// <summary>
    ///     Gets the current user's IP address.
    /// </summary>
    string? IpAddress { get; }

    /// <summary>
    ///     Gets the current user's user agent.
    /// </summary>
    string? UserAgent { get; }

    /// <summary>
    ///     Gets the current user's roles.
    /// </summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    ///     Gets additional user claims.
    /// </summary>
    IReadOnlyDictionary<string, string> Claims { get; }
}
