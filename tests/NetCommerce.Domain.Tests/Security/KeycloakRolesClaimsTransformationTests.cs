#region

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

#endregion

namespace NetCommerce.Domain.Tests.Security;

/// <summary>
///     Unit tests for KeycloakRolesClaimsTransformation.
///     Verifies that Keycloak's nested JSON role structure is correctly flattened
///     into standard .NET ClaimsPrincipal roles.
/// </summary>
public class KeycloakRolesClaimsTransformationTests
{
    private readonly IClaimsTransformation _transformer;

    public KeycloakRolesClaimsTransformationTests()
    {
        _transformer = new KeycloakRolesClaimsTransformation("netcommerce-api");
    }

    [Fact]
    public async Task TransformAsync_WithRealmRoles_FlattensToRoleClaims()
    {
        // Arrange
        string realmAccessJson = """{"roles":["admin","customer","vendor"]}""";
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("realm_access", realmAccessJson));
        var principal = new ClaimsPrincipal(identity);

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert
        result.IsInRole("admin").ShouldBeTrue();
        result.IsInRole("customer").ShouldBeTrue();
        result.IsInRole("vendor").ShouldBeTrue();
        result.IsInRole("nonexistent").ShouldBeFalse();
    }

    [Fact]
    public async Task TransformAsync_WithClientRoles_FlattensToPermissionsClaims()
    {
        // Arrange
        string resourceAccessJson = """
                                    {
                                        "netcommerce-api": {
                                            "roles": ["catalog:read", "catalog:write", "orders:read"]
                                        }
                                    }
                                    """;
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("resource_access", resourceAccessJson));
        var principal = new ClaimsPrincipal(identity);

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert
        result.HasClaim("permissions", "catalog:read").ShouldBeTrue();
        result.HasClaim("permissions", "catalog:write").ShouldBeTrue();
        result.HasClaim("permissions", "orders:read").ShouldBeTrue();
        result.HasClaim("permissions", "orders:write").ShouldBeFalse();
    }

    [Fact]
    public async Task TransformAsync_WithBothRealmAndClientRoles_FlattensAll()
    {
        // Arrange
        string realmAccessJson = """{"roles":["admin"]}""";
        string resourceAccessJson = """
                                    {
                                        "netcommerce-api": {
                                            "roles": ["catalog:read"]
                                        },
                                        "other-service": {
                                            "roles": ["service:access"]
                                        }
                                    }
                                    """;
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("realm_access", realmAccessJson));
        identity.AddClaim(new Claim("resource_access", resourceAccessJson));
        var principal = new ClaimsPrincipal(identity);

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert
        // Realm role
        result.IsInRole("admin").ShouldBeTrue();
        // API client role
        result.HasClaim("permissions", "catalog:read").ShouldBeTrue();
        // Other service role (with namespace prefix)
        result.HasClaim("permissions", "other-service:service:access").ShouldBeTrue();
    }

    [Fact]
    public async Task TransformAsync_WithNoRoleClaims_ReturnsUnmodifiedPrincipal()
    {
        // Arrange
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "user123"));
        identity.AddClaim(new Claim("email", "user@example.com"));
        var principal = new ClaimsPrincipal(identity);

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert
        result.Claims.Count().ShouldBe(2);
        result.HasClaim("sub", "user123").ShouldBeTrue();
        result.HasClaim("email", "user@example.com").ShouldBeTrue();
    }

    [Fact]
    public async Task TransformAsync_WithMalformedJson_DoesNotThrow()
    {
        // Arrange
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("realm_access", "not valid json {{{"));
        var principal = new ClaimsPrincipal(identity);

        // Act & Assert - should not throw
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task TransformAsync_WithEmptyRolesArray_AddsNoRoleClaims()
    {
        // Arrange
        string realmAccessJson = """{"roles":[]}""";
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("realm_access", realmAccessJson));
        var principal = new ClaimsPrincipal(identity);

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert
        result.Claims.Where(c => c.Type == ClaimTypes.Role).ShouldBeEmpty();
    }

    [Fact]
    public async Task TransformAsync_PreventsDuplicateClaims()
    {
        // Arrange - role appears in both realm and client access
        string realmAccessJson = """{"roles":["admin"]}""";
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("realm_access", realmAccessJson));
        // Add the same role again to test deduplication
        identity.AddClaim(new Claim("realm_access", realmAccessJson));
        var principal = new ClaimsPrincipal(identity);

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert - should only have one "admin" role claim
        IEnumerable<Claim> adminClaims = result.Claims.Where(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        adminClaims.Count().ShouldBe(1);
    }

    [Fact]
    public async Task TransformAsync_WithNullIdentity_ReturnsOriginalPrincipal()
    {
        // Arrange
        var principal = new ClaimsPrincipal();

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert
        result.ShouldBeSameAs(principal);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("customer")]
    [InlineData("vendor")]
    [InlineData("super-admin")]
    [InlineData("read-only")]
    public async Task TransformAsync_WithVariousRoleNames_AllAreFlattenedCorrectly(string roleName)
    {
        // Arrange
        string realmAccessJson = $$"""{"roles":["{{roleName}}"]}""";
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("realm_access", realmAccessJson));
        var principal = new ClaimsPrincipal(identity);

        // Act
        ClaimsPrincipal result = await _transformer.TransformAsync(principal);

        // Assert
        result.IsInRole(roleName).ShouldBeTrue();
    }
}
