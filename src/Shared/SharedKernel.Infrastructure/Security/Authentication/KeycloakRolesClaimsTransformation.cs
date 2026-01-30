// =============================================================================
// DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.OidcRoleClaimsTransformation
// This file forwards to the canonical implementation in Kernel.Security.
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using KernelAuth = NetCommerce.Kernel.Security.Authentication;

namespace NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

/// <summary>
///     DEPRECATED: Use NetCommerce.Kernel.Security.Authentication.OidcRoleClaimsTransformation instead.
///     This class forwards to the canonical implementation.
/// </summary>
[Obsolete("Use NetCommerce.Kernel.Security.Authentication.OidcRoleClaimsTransformation instead.")]
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    private readonly KernelAuth.OidcRoleClaimsTransformation _inner;

    public KeycloakRolesClaimsTransformation(string? apiClientId = null)
    {
        _inner = new KernelAuth.OidcRoleClaimsTransformation(apiClientId);
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        return _inner.TransformAsync(principal);
    }
}
