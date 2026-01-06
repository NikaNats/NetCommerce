#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NetCommerce.Kernel.Application;

namespace NetCommerce.Kernel.Security;

/// <summary>
///     HTTP-based user context that extracts user information from HttpContext.
/// </summary>
public class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;
    private HttpContext? Context => _httpContextAccessor.HttpContext;

    public string UserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User?.FindFirst("sub")?.Value
                         ?? "unknown";

    public string Role => User?.FindFirst(ClaimTypes.Role)?.Value
                       ?? User?.FindFirst("roles")?.Value
                       ?? "Guest";

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Context?.Request.Headers.UserAgent.ToString();

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
