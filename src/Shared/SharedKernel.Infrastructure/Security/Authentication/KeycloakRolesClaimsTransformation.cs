#region

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

#endregion

namespace NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

/// <summary>
///     Keycloak RBAC Claims Transformation.
///     Flattens Keycloak's nested JSON role structure into standard .NET ClaimsPrincipal roles.
///     Keycloak stores roles in:
///     - realm_access.roles: Global realm roles (admin, customer, vendor)
///     - resource_access.{clientId}.roles: Client-specific roles (catalog:read, orders:write)
///     This transformation extracts and maps them to:
///     - ClaimTypes.Role: For [Authorize(Roles = "admin")]
///     - "permissions": For fine-grained permission checks
/// </summary>
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    private const string RealmAccessClaim = "realm_access";
    private const string ResourceAccessClaim = "resource_access";
    private const string RolesProperty = "roles";
    private const string RolesClaim = "roles";
    private const string PermissionsClaim = "permissions";
    private const string DefaultApiClientId = "netcommerce-api";

    private readonly string _apiClientId;

    public KeycloakRolesClaimsTransformation(string? apiClientId = null)
    {
        _apiClientId = apiClientId ?? DefaultApiClientId;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity) return Task.FromResult(principal);

        // Extract and flatten realm roles (global roles)
        ExtractRealmRoles(principal, identity);

        // Extract and flatten client roles (API-specific permissions)
        ExtractClientRoles(principal, identity);

        return Task.FromResult(principal);
    }

    /// <summary>
    ///     Extracts realm-level roles from the realm_access claim.
    ///     These become standard .NET roles for [Authorize(Roles = "...")].
    /// </summary>
    private static void ExtractRealmRoles(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        Claim? realmAccessClaim = principal.FindFirst(RealmAccessClaim);
        if (realmAccessClaim?.Value is not { Length: > 0 } realmJson) return;

        try
        {
            ParseAndAddRoles(identity, realmJson, RolesClaim);
        }
        catch (JsonException)
        {
            // Malformed JSON - log but don't fail authentication
        }
    }

    /// <summary>
    ///     Extracts client-level roles from the resource_access claim.
    ///     These become permissions for fine-grained access control.
    /// </summary>
    private void ExtractClientRoles(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        Claim? resourceAccessClaim = principal.FindFirst(ResourceAccessClaim);
        if (resourceAccessClaim?.Value is not { Length: > 0 } resourceJson) return;

        try
        {
            using var doc = JsonDocument.Parse(resourceJson);

            // Extract roles for this specific API client
            if (doc.RootElement.TryGetProperty(_apiClientId, out JsonElement clientElement))
                ParseAndAddRoles(identity, clientElement.GetRawText(), PermissionsClaim);

            // Also extract roles from any other clients the user has access to
            // This supports multi-service architectures
            foreach (JsonProperty client in doc.RootElement.EnumerateObject())
            {
                if (client.Name == _apiClientId) continue;

                if (client.Value.TryGetProperty(RolesProperty, out JsonElement roles))
                    foreach (JsonElement role in roles.EnumerateArray())
                    {
                        string? roleValue = role.GetString();
                        if (!string.IsNullOrEmpty(roleValue))
                            // Prefix with client name for namespace separation
                            identity.AddClaim(new Claim(PermissionsClaim, $"{client.Name}:{roleValue}"));
                    }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON - log but don't fail authentication
        }
    }

    /// <summary>
    ///     Parses a JSON object containing a "roles" array and adds each role as a claim.
    /// </summary>
    private static void ParseAndAddRoles(ClaimsIdentity identity, string json, string claimType)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty(RolesProperty, out JsonElement roles)) return;

        foreach (JsonElement role in roles.EnumerateArray())
        {
            string? roleValue = role.GetString();
            if (!string.IsNullOrEmpty(roleValue))
                // Prevent duplicate claims
                if (!identity.HasClaim(claimType, roleValue))
                    identity.AddClaim(new Claim(claimType, roleValue));
        }
    }
}
