#nullable enable
using System.Security.Claims;

namespace NetCommerce.Api.Endpoints.Auth;

/// <summary>
///     Session introspection handlers (JWT claims mapping). Moved verbatim from
///     AuthEndpoints.cs — claim fallbacks (sub → NameIdentifier, preferred_username →
///     Identity.Name, tenant_id → tid) and the full response shape are unchanged.
/// </summary>
public static class SessionHandlers
{
    public static IResult GetSessionInfo(HttpContext httpContext)
    {
        var user = httpContext.User;

        var response = new SessionInfoResponse
        {
            // Identity precedence: standard OIDC 'sub' first, then the
            // Keycloak 'user_id' protocol mapper (netcommerce.api scope).
            // The mapper exists because the Keycloak deployment backing this
            // API omits 'sub' from access tokens (proven live against both the
            // password and authorization-code flows); without it UserId is
            // "unknown" for every caller.
            UserId = user.FindFirst("sub")?.Value
                     ?? user.FindFirst("user_id")?.Value
                     ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown",
            Username = user.FindFirst("preferred_username")?.Value
                       ?? user.Identity?.Name ?? "unknown",
            Email = user.FindFirst("email")?.Value,
            RealmRoles = user.FindAll(ClaimTypes.Role)
                .Concat(user.FindAll("roles"))
                .Select(c => c.Value)
                .Distinct()
                .ToList(),
            ClientRoles = user.FindAll("client_roles")
                .Select(c => c.Value)
                .Distinct()
                .ToList(),
            TenantId = user.FindFirst("tenant_id")?.Value ?? user.FindFirst("tid")?.Value,
            TokenExpiresAt = GetTokenExpiry(user),
            AuthenticatedAt = GetAuthTime(user),
            SessionState = user.FindFirst("session_state")?.Value
        };

        return Results.Ok(response);
    }

    public static DateTime? GetTokenExpiry(ClaimsPrincipal user)
    {
        var exp = user.FindFirst("exp")?.Value;
        if (exp is not null && long.TryParse(exp, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return null;
    }

    public static DateTime? GetAuthTime(ClaimsPrincipal user)
    {
        var authTime = user.FindFirst("auth_time")?.Value;
        if (authTime is not null && long.TryParse(authTime, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return null;
    }
}
