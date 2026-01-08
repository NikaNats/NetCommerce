#nullable enable
using Microsoft.AspNetCore.Http;
using NetCommerce.Kernel.Application;
using System.Security.Claims;

namespace NetCommerce.Kernel.Security;

/// <summary>
///     Resolves TenantId from HTTP Headers or User Claims.
/// </summary>
public class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string TenantHeader = "X-Tenant-ID";
    private const string TenantClaim = "tenant_id"; // or "tid"

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? TenantId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is null) return null;

            // 1. Try User Claims (Secure)
            var claimId = context.User?.FindFirst(TenantClaim)?.Value;
            if (!string.IsNullOrEmpty(claimId)) return claimId;

            // 2. Try Header (Useful for Service-to-Service or Testing)
            if (context.Request.Headers.TryGetValue(TenantHeader, out var headerId))
            {
                return headerId.ToString();
            }

            return null;
        }
    }

    public bool HasTenant => !string.IsNullOrEmpty(TenantId);
}
