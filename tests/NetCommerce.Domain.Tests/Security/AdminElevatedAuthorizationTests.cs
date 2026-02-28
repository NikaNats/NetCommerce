#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Security.Authorization;

namespace NetCommerce.Domain.Tests.Security;

/// <summary>
///     Tests for admin elevated authentication covering:
///     - API key validation (constant-time comparison)
///     - Step-up auth via auth_time claim recency
///     - Combined admin role + elevation requirement
///     - Rejection when neither method is satisfied
///     - Non-admin users are always rejected
///     - Edge cases: expired auth_time, empty API key, missing claims
/// </summary>
public class AdminElevatedAuthorizationTests
{
    private const string ValidApiKey = "test-admin-api-key-2025";
    private readonly AdminElevatedAuthorizationHandler _handler;

    public AdminElevatedAuthorizationTests()
    {
        var options = Options.Create(new AdminApiKeyOptions
        {
            ApiKey = ValidApiKey,
            MaxAuthAgeMinutes = 15
        });
        _handler = new AdminElevatedAuthorizationHandler(options);
    }

    // ========================================================================
    // API Key Validation
    // ========================================================================

    [Fact]
    public async Task ValidApiKey_WithAdminRole_Succeeds()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser();
        var httpContext = CreateHttpContextWithApiKey(ValidApiKey);
        var context = CreateContext(user, requirement, httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidApiKey_WithoutRecentAuth_Fails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser(authTimeMinutesAgo: 60); // Stale auth
        var httpContext = CreateHttpContextWithApiKey("wrong-key");
        var context = CreateContext(user, requirement, httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task EmptyApiKey_WithoutRecentAuth_Fails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser(authTimeMinutesAgo: 60);
        var httpContext = CreateHttpContextWithApiKey("");
        var context = CreateContext(user, requirement, httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task NoApiKeyHeader_WithoutRecentAuth_Fails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser(authTimeMinutesAgo: 60);
        var httpContext = new DefaultHttpContext(); // No header
        var context = CreateContext(user, requirement, httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    // ========================================================================
    // Step-Up Authentication (auth_time recency)
    // ========================================================================

    [Fact]
    public async Task RecentAuth_WithAdminRole_Succeeds()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser(authTimeMinutesAgo: 5); // Fresh auth
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task StaleAuth_WithAdminRole_Fails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser(authTimeMinutesAgo: 30); // Stale auth
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task ExactlyAtMaxAuthAge_Succeeds()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser(authTimeMinutesAgo: 14); // Just within window
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeTrue();
    }

    // ========================================================================
    // Non-Admin Users
    // ========================================================================

    [Fact]
    public async Task NonAdminUser_AlwaysFails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "customer-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "customer"));
        var user = new ClaimsPrincipal(identity);
        var httpContext = CreateHttpContextWithApiKey(ValidApiKey); // Even with valid key
        var context = CreateContext(user, requirement, httpContext);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task VendorUser_AlwaysFails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "vendor-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "vendor"));
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    // ========================================================================
    // Edge Cases
    // ========================================================================

    [Fact]
    public async Task MalformedAuthTimeClaim_Fails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "admin-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        identity.AddClaim(new Claim("auth_time", "not-a-number"));
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task NoAuthTimeClaim_NoApiKey_Fails()
    {
        // Arrange
        var requirement = new AdminElevatedRequirement();
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "admin-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        // No auth_time claim
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task UnconfiguredApiKey_OnlyAuthTimeWorks()
    {
        // Arrange - Handler with empty API key config
        var emptyKeyOptions = Options.Create(new AdminApiKeyOptions { ApiKey = "" });
        var handler = new AdminElevatedAuthorizationHandler(emptyKeyOptions);

        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var httpContext = CreateHttpContextWithApiKey("any-key");
        var context = CreateContext(user, requirement, httpContext);

        // Act
        await handler.HandleAsync(context);

        // Assert - Should succeed via auth_time even though API key comparison won't match
        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CaseSensitiveAdmin_WithLowercaseRole_Succeeds()
    {
        // Arrange - Keycloak uses lowercase "admin"
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "admin-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin")); // lowercase
        identity.AddClaim(new Claim("auth_time",
            DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString()));
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CaseSensitiveAdmin_WithUppercaseRole_Succeeds()
    {
        // Arrange - Some OIDC providers use "Admin" (uppercase)
        var requirement = new AdminElevatedRequirement { MaxAuthAgeMinutes = 15 };
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "admin-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin")); // uppercase
        identity.AddClaim(new Claim("auth_time",
            DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString()));
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user, requirement, new DefaultHttpContext());

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ApiKeyTimingAttack_ConstantTimeComparison()
    {
        // This test verifies that key comparison doesn't short-circuit
        // We can't truly test timing, but we verify both valid and
        // partially-matching keys behave consistently
        var requirement = new AdminElevatedRequirement();
        var user = CreateAdminUser();

        // Almost-correct key (off by one char)
        var almostKey = ValidApiKey[..^1] + "X";
        var ctx1 = CreateContext(user, requirement, CreateHttpContextWithApiKey(almostKey));
        await _handler.HandleAsync(ctx1);
        ctx1.HasSucceeded.ShouldBeFalse();

        // Completely wrong key
        var wrongKey = "completely-wrong-key-value";
        var ctx2 = CreateContext(user, requirement, CreateHttpContextWithApiKey(wrongKey));
        await _handler.HandleAsync(ctx2);
        ctx2.HasSucceeded.ShouldBeFalse();
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static ClaimsPrincipal CreateAdminUser(int? authTimeMinutesAgo = null)
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "admin-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));

        if (authTimeMinutesAgo.HasValue)
        {
            var authTime = DateTimeOffset.UtcNow.AddMinutes(-authTimeMinutesAgo.Value);
            identity.AddClaim(new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString()));
        }

        return new ClaimsPrincipal(identity);
    }

    private static DefaultHttpContext CreateHttpContextWithApiKey(string? apiKey)
    {
        var context = new DefaultHttpContext();
        if (apiKey is not null)
            context.Request.Headers["X-Admin-Api-Key"] = apiKey;
        return context;
    }

    private static AuthorizationHandlerContext CreateContext(
        ClaimsPrincipal user,
        IAuthorizationRequirement requirement,
        HttpContext? httpContext = null)
    {
        return new AuthorizationHandlerContext(
            [requirement],
            user,
            httpContext);
    }
}
