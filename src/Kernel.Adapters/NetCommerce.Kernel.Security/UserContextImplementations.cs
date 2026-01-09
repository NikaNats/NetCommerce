#nullable enable
using System.Security.Principal;
using System.Security.Claims;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Kernel.Security;

/// <summary>
///     Represents the 'System' user for background processing.
///     Uses GenericPrincipal to satisfy .NET Identity requirements.
/// </summary>
public sealed class SystemUserContext : IUserContext
{
    private readonly ClaimsPrincipal _systemPrincipal;

    public SystemUserContext(string systemName = "system-worker", string? tenantId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, systemName),
            new(ClaimTypes.Role, "System"),
            new("sub", systemName)
        };

        if (tenantId != null) claims.Add(new Claim("tenant_id", tenantId));

        var identity = new ClaimsIdentity(claims, "SystemAuth");
        _systemPrincipal = new ClaimsPrincipal(identity);
    }

    public ClaimsPrincipal User => _systemPrincipal;
    public string UserId => _systemPrincipal.FindFirst(ClaimTypes.NameIdentifier)!.Value;
    public string? TenantId => GetClaim("tenant_id");
    public bool IsAuthenticated => true;
    public string? GetClaim(string claimType) => _systemPrincipal.FindFirst(claimType)?.Value;
    public IEnumerable<string> Roles => _systemPrincipal.FindAll(ClaimTypes.Role).Select(c => c.Value);
    public bool IsInRole(string role) => _systemPrincipal.IsInRole(role);
}

/// <summary>
///     Development/Testing stub that simulates an admin user.
/// </summary>
public class DevelopmentUserContext : IUserContext
{
    private readonly ClaimsPrincipal _devPrincipal;

    public DevelopmentUserContext(string userId = "dev_user", string role = "Admin", string? tenantId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
            new("sub", userId)
        };

        if (tenantId != null) claims.Add(new Claim("tenant_id", tenantId));

        var identity = new ClaimsIdentity(claims, "DevelopmentAuth");
        _devPrincipal = new ClaimsPrincipal(identity);
    }

    public ClaimsPrincipal User => _devPrincipal;
    public string UserId => GetClaim(ClaimTypes.NameIdentifier) ?? "dev_user";
    public string? TenantId => GetClaim("tenant_id");
    public bool IsAuthenticated => true;
    public string? GetClaim(string claimType) => _devPrincipal.FindFirst(claimType)?.Value;
    public IEnumerable<string> Roles => _devPrincipal.FindAll(ClaimTypes.Role).Select(c => c.Value);
    public bool IsInRole(string role) => GetClaim(ClaimTypes.Role) == role;
}

/// <summary>
///     Anonymous user context for unauthenticated requests.
/// </summary>
public class AnonymousUserContext : IUserContext
{
    private readonly ClaimsPrincipal _anonymousPrincipal;

    public AnonymousUserContext(string? ipAddress = null, string? userAgent = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "anonymous"),
            new(ClaimTypes.Role, "Guest"),
            new("sub", "anonymous")
        };

        if (ipAddress != null) claims.Add(new Claim("ip_address", ipAddress));
        if (userAgent != null) claims.Add(new Claim("user_agent", userAgent));

        var identity = new ClaimsIdentity(claims); // No AuthType = Unauthenticated
        _anonymousPrincipal = new ClaimsPrincipal(identity);
    }

    public ClaimsPrincipal User => _anonymousPrincipal;
    public string UserId => "anonymous";
    public string? TenantId => null;
    public bool IsAuthenticated => false;
    public string? GetClaim(string claimType) => _anonymousPrincipal.FindFirst(claimType)?.Value;
    public IEnumerable<string> Roles => _anonymousPrincipal.FindAll(ClaimTypes.Role).Select(c => c.Value);
    public bool IsInRole(string role) => role == "Guest";
}

/// <summary>
///     System/Background Job tenant context.
///     Used when running background jobs for a specific tenant.
/// </summary>
public class SystemTenantContext : ITenantContext
{
    public SystemTenantContext(string tenantId)
    {
        TenantId = tenantId;
    }

    public string? TenantId { get; }
    public bool HasTenant => !string.IsNullOrEmpty(TenantId);
}

/// <summary>
///     Development/Testing stub that simulates a tenant context.
/// </summary>
public class DevelopmentTenantContext : ITenantContext
{
    public DevelopmentTenantContext(string tenantId = "dev_tenant")
    {
        TenantId = tenantId;
    }

    public string? TenantId { get; }
    public bool HasTenant => !string.IsNullOrEmpty(TenantId);
}
