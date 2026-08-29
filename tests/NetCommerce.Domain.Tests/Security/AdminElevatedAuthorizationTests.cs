#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Security.Authorization;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Domain.Tests.Security;

public class AdminElevatedAuthorizationTests
{
    private const string ValidApiKey = "test-admin-api-key-minimum-32-characters!";
    private readonly ILogger<AdminElevatedAuthorizationHandler> _logger =
        Substitute.For<ILogger<AdminElevatedAuthorizationHandler>>();

    private AdminElevatedAuthorizationHandler CreateHandler(
        string apiKey = ValidApiKey,
        AdminElevatedSecurityMode mode = AdminElevatedSecurityMode.Strict,
        int maxAuthAge = 15)
    {
        var options = Options.Create(new AdminElevatedAuthOptions
        {
            ApiKey = apiKey,
            SecurityMode = mode,
            MaxAuthAgeMinutes = maxAuthAge
        });
        return new AdminElevatedAuthorizationHandler(options, _logger);
    }

    // ═══════════════════════════════════════════════════════════
    // FAIL-CLOSED: Unconfigured API key
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task EmptyApiKey_StrictMode_ShouldDeny()
    {
        var handler = CreateHandler(apiKey: "", mode: AdminElevatedSecurityMode.Strict);
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(ValidApiKey));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse(
            "Empty API key in Strict mode must deny all elevated access");
    }

    [Fact]
    public async Task EmptyApiKey_FlexibleMode_ShouldDeny()
    {
        var handler = CreateHandler(apiKey: "", mode: AdminElevatedSecurityMode.Flexible);
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(null));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse(
            "Empty API key in Flexible mode must deny — no silent fallthrough");
    }

    [Fact]
    public async Task ShortApiKey_ShouldDeny()
    {
        var handler = CreateHandler(apiKey: "too-short", mode: AdminElevatedSecurityMode.Strict);
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext("too-short"));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse(
            "API key shorter than 32 chars must be treated as unconfigured");
    }

    // ═══════════════════════════════════════════════════════════
    // STRICT MODE: Both factors required
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Strict_ValidKeyAndFreshAuth_ShouldSucceed()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Strict);
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(ValidApiKey));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Strict_ValidKeyButStaleAuth_ShouldDeny()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Strict);
        var user = CreateAdminUser(authTimeMinutesAgo: 30); // Stale
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(ValidApiKey));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse(
            "Strict mode requires BOTH valid key AND fresh auth_time");
    }

    [Fact]
    public async Task Strict_FreshAuthButInvalidKey_ShouldDeny()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Strict);
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext("wrong-key-that-is-long-enough-to-be-32-chars!"));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse(
            "Strict mode requires BOTH valid key AND fresh auth_time");
    }

    [Fact]
    public async Task Strict_NoKeyHeader_ShouldDeny()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Strict);
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var context = CreateContext(user, new AdminElevatedRequirement(), new DefaultHttpContext());

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════
    // FLEXIBLE MODE: Either factor sufficient
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Flexible_ValidKeyOnly_ShouldSucceed()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Flexible);
        var user = CreateAdminUser(authTimeMinutesAgo: 60); // Stale auth
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(ValidApiKey));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue(
            "Flexible mode: valid API key alone should suffice");
    }

    [Fact]
    public async Task Flexible_FreshAuthOnly_ShouldSucceed()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Flexible);
        var user = CreateAdminUser(authTimeMinutesAgo: 5);
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(null));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue(
            "Flexible mode: fresh auth_time alone should suffice");
    }

    // ═══════════════════════════════════════════════════════════
    // ROLE GATE
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task NonAdminUser_AlwaysFails()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Flexible);
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "customer-user"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "customer"));
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(ValidApiKey));

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════
    // TIMING ATTACK RESISTANCE
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task InvalidKey_ConstantTimeComparison()
    {
        var handler = CreateHandler(mode: AdminElevatedSecurityMode.Flexible);
        var user = CreateAdminUser(authTimeMinutesAgo: 60);

        var almostKey = ValidApiKey[..^1] + "X";
        var wrongKey = "completely-wrong-key-that-is-32-chars!";

        var ctx1 = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(almostKey));
        var ctx2 = CreateContext(user, new AdminElevatedRequirement(), CreateHttpContext(wrongKey));

        await handler.HandleAsync(ctx1);
        await handler.HandleAsync(ctx2);

        ctx1.HasSucceeded.ShouldBeFalse();
        ctx2.HasSucceeded.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════

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

    private static DefaultHttpContext CreateHttpContext(string? apiKey)
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
        return new AuthorizationHandlerContext([requirement], user, httpContext);
    }
}
