#nullable enable

using NetCommerce.SharedKernel.Application;

namespace NetCommerce.SharedKernel.Infrastructure.Security;

/// <summary>
///     2025 Elite Pattern: System/Background Job user context.
///     Used when no authenticated user exists (e.g., scheduled jobs, event handlers).
///     
///     In production, you'd have:
///     - HttpUserContext (extracts from HttpContext.User)
///     - ApiKeyUserContext (validates API key headers)
///     - SystemUserContext (this one - for background processes)
/// </summary>
public class SystemUserContext : IUserContext
{
    public string UserId => "system@netcommerce.com";
    public string Role => "System";
    public string? IpAddress => null;
    public string? UserAgent => "NetCommerce/BackgroundJob";
    public bool IsAuthenticated => true;
}

/// <summary>
///     Development/Testing stub that simulates an admin user.
///     Replace with HttpUserContext in production.
/// </summary>
public class DevelopmentUserContext : IUserContext
{
    private readonly string _userId;
    private readonly string _role;

    public DevelopmentUserContext(string userId = "dev_user", string role = "Admin")
    {
        _userId = userId;
        _role = role;
    }

    public string UserId => _userId;
    public string Role => _role;
    public string? IpAddress => "127.0.0.1";
    public string? UserAgent => "NetCommerce/Development";
    public bool IsAuthenticated => true;
}
