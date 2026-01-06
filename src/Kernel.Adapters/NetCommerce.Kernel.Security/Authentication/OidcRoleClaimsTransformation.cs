#nullable enable
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace NetCommerce.Kernel.Security.Authentication;

/// <summary>
///     OIDC RBAC Claims Transformation.
///     Flattens nested JSON role structures (Keycloak, Auth0, etc.) into standard .NET ClaimsPrincipal roles.
///     Supports:
///     - realm_access.roles: Global realm roles
///     - resource_access.{clientId}.roles: Client-specific roles
/// </summary>
public sealed class OidcRoleClaimsTransformation : IClaimsTransformation
{
    private const string RealmAccessClaim = "realm_access";
    private const string ResourceAccessClaim = "resource_access";
    private const string RolesProperty = "roles";
    private const string RolesClaim = "roles";
    private const string PermissionsClaim = "permissions";

    private readonly string _apiClientId;

    public OidcRoleClaimsTransformation(string? apiClientId = null)
    {
        _apiClientId = apiClientId ?? "api";
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity)
            return Task.FromResult(principal);

        ExtractRealmRoles(principal, identity);
        ExtractClientRoles(principal, identity);

        return Task.FromResult(principal);
    }

    private static void ExtractRealmRoles(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var realmAccessClaim = principal.FindFirst(RealmAccessClaim);
        if (realmAccessClaim?.Value is not { Length: > 0 } realmJson)
            return;

        try
        {
            ParseAndAddRoles(identity, realmJson, RolesClaim);
        }
        catch (JsonException)
        {
            // Malformed JSON - log but don't fail authentication
        }
    }

    private void ExtractClientRoles(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var resourceAccessClaim = principal.FindFirst(ResourceAccessClaim);
        if (resourceAccessClaim?.Value is not { Length: > 0 } resourceJson)
            return;

        try
        {
            using var doc = JsonDocument.Parse(resourceJson);

            if (doc.RootElement.TryGetProperty(_apiClientId, out var clientElement))
                ParseAndAddRoles(identity, clientElement.GetRawText(), PermissionsClaim);

            foreach (var client in doc.RootElement.EnumerateObject())
            {
                if (client.Name == _apiClientId)
                    continue;

                if (client.Value.TryGetProperty(RolesProperty, out var roles))
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        var roleValue = role.GetString();
                        if (!string.IsNullOrEmpty(roleValue))
                            identity.AddClaim(new Claim(PermissionsClaim, $"{client.Name}:{roleValue}"));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON - log but don't fail authentication
        }
    }

    private static void ParseAndAddRoles(ClaimsIdentity identity, string json, string claimType)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty(RolesProperty, out var roles))
            return;

        foreach (var role in roles.EnumerateArray())
        {
            var roleValue = role.GetString();
            if (!string.IsNullOrEmpty(roleValue))
            {
                identity.AddClaim(new Claim(claimType, roleValue));

                // Also add as standard Role claim for [Authorize(Roles = "...")]
                if (claimType == RolesClaim)
                    identity.AddClaim(new Claim(ClaimTypes.Role, roleValue));
            }
        }
    }
}
