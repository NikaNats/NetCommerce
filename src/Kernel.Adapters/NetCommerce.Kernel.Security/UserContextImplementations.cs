#nullable enable
using NetCommerce.Kernel.Application;

namespace NetCommerce.Kernel.Security;

/// <summary>
///     System/Background Job user context.
///     Used when no authenticated user exists (e.g., scheduled jobs, event handlers).
/// </summary>
public class SystemUserContext : IUserContext
{
    private readonly string _systemIdentifier;

    public SystemUserContext(string? systemIdentifier = null)
    {
        _systemIdentifier = systemIdentifier ?? "system";
    }

    public string UserId => $"{_systemIdentifier}@background";
    public string Role => "System";
    public string? IpAddress => null;
    public string? UserAgent => "Kernel/BackgroundJob";
    public bool IsAuthenticated => true;
}

/// <summary>
///     Development/Testing stub that simulates an admin user.
/// </summary>
public class DevelopmentUserContext : IUserContext
{
    public DevelopmentUserContext(string userId = "dev_user", string role = "Admin")
    {
        UserId = userId;
        Role = role;
    }

    public string UserId { get; }
    public string Role { get; }
    public string? IpAddress => "127.0.0.1";
    public string? UserAgent => "Kernel/Development";
    public bool IsAuthenticated => true;
}

/// <summary>
///     Anonymous user context for unauthenticated requests.
/// </summary>
public class AnonymousUserContext : IUserContext
{
    public string UserId => "anonymous";
    public string Role => "Guest";
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public bool IsAuthenticated => false;
}
