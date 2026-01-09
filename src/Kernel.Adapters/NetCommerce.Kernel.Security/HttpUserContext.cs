#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Kernel.Security;

public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Cache common values to avoid repeated Claim lookups in a single request
    private string? _cachedUserId;
    private string? _cachedTenantId;

    public HttpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User
                                   ?? new ClaimsPrincipal(new ClaimsIdentity());

    public bool IsAuthenticated => User.Identity?.IsAuthenticated ?? false;

    public string UserId => _cachedUserId ??=
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? "anonymous";

    public string? TenantId => _cachedTenantId ??=
        User.FindFirst("tenant_id")?.Value
        ?? User.FindFirst("tid")?.Value;

    public string? GetClaim(string claimType)
    {
        var claim = User.FindFirst(claimType)?.Value;
        if (claim != null) return claim;

        // Fallback for technical metadata
        return claimType switch
        {
            "ip_address" => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            "user_agent" => _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString(),
            _ => null
        };
    }

    public IEnumerable<string> Roles => User.FindAll(ClaimTypes.Role).Select(c => c.Value);

    public bool IsInRole(string role) => User.IsInRole(role);
}
