#nullable enable

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Security.Authorization;
using Shouldly;

namespace NetCommerce.Integration.Tests.Security;

[Trait("Category", "SecurityPenetration")]
public sealed class AdminElevatedStepUpAndVerbTunnelingTests
{
    private const string ValidSecretApiKey = "production-admin-key-minimum-32-chars-length!";

    private static AdminElevatedAuthorizationHandler CreateHandler(AdminElevatedSecurityMode mode = AdminElevatedSecurityMode.Strict)
    {
        var options = Options.Create(new AdminElevatedAuthOptions
        {
            ApiKey = ValidSecretApiKey,
            MaxAuthAgeMinutes = 15,
            SecurityMode = mode
        });

        return new AdminElevatedAuthorizationHandler(options, NullLogger<AdminElevatedAuthorizationHandler>.Instance);
    }

    [Fact]
    public async Task AdminJwt_WithoutApiKeyHeader_MustBeDeniedWith403()
    {
        var handler = CreateHandler(AdminElevatedSecurityMode.Strict);
        var adminUser = CreatePrincipal(roles: ["admin"], authTimeMinutesAgo: 5);

        // HTTP Context with NO X-Admin-Api-Key header
        var httpContext = new DefaultHttpContext();
        var authContext = new AuthorizationHandlerContext([new AdminElevatedRequirement()], adminUser, httpContext);

        await handler.HandleAsync(authContext);

        authContext.HasSucceeded.ShouldBeFalse("AdminElevated requirement succeeded without providing X-Admin-Api-Key header!");
    }

    [Fact]
    public async Task AdminJwt_WithStaleAuthTime_MustBeDenied_StepUpRequired()
    {
        var handler = CreateHandler(AdminElevatedSecurityMode.Strict);
        // Auth occurred 20 minutes ago (exceeds 15-minute maxAuthAge)
        var adminUser = CreatePrincipal(roles: ["admin"], authTimeMinutesAgo: 20);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Api-Key"] = ValidSecretApiKey;

        var authContext = new AuthorizationHandlerContext([new AdminElevatedRequirement()], adminUser, httpContext);

        await handler.HandleAsync(authContext);

        authContext.HasSucceeded.ShouldBeFalse("AdminElevated requirement allowed stale authentication (>15 minutes) without re-authentication challenge!");
    }

    [Fact]
    public async Task AdminJwt_WithValidApiKey_AndFreshAuthTime_MustSucceed()
    {
        var handler = CreateHandler(AdminElevatedSecurityMode.Strict);
        var adminUser = CreatePrincipal(roles: ["admin"], authTimeMinutesAgo: 2); // Fresh

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Api-Key"] = ValidSecretApiKey;

        var authContext = new AuthorizationHandlerContext([new AdminElevatedRequirement()], adminUser, httpContext);

        await handler.HandleAsync(authContext);

        authContext.HasSucceeded.ShouldBeTrue();
    }

    [Fact]
    public async Task VerbTunneling_MethodOverrideHeader_MustNotBypassEndpointPolicy()
    {
        var filter = new AdminElevatedEndpointFilter(Options.Create(new AdminElevatedAuthOptions
        {
            ApiKey = ValidSecretApiKey,
            MaxAuthAgeMinutes = 15,
            SecurityMode = AdminElevatedSecurityMode.Strict
        }), NullLogger<AdminElevatedEndpointFilter>.Instance);

        // Attacker attempts GET with X-HTTP-Method-Override: POST to bypass policy
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Headers["X-HTTP-Method-Override"] = "POST";
        httpContext.User = CreatePrincipal(roles: ["customer"]); // Not an admin

        var filterContext = new TestEndpointFilterInvocationContext(httpContext);
        var endpointExecuted = false;

        var result = await filter.InvokeAsync(filterContext, _ =>
        {
            endpointExecuted = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        endpointExecuted.ShouldBeFalse("Verb tunneling bypassed endpoint authorization filter!");

        var problemResult = result.ShouldBeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        problemResult.ProblemDetails.Type.ShouldBe("https://docs.netcommerce.io/errors/admin-required");
    }

    private static ClaimsPrincipal CreatePrincipal(string[] roles, int? authTimeMinutesAgo = null)
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", Guid.NewGuid().ToString()));

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        if (authTimeMinutesAgo.HasValue)
        {
            var authTime = DateTimeOffset.UtcNow.AddMinutes(-authTimeMinutesAgo.Value).ToUnixTimeSeconds();
            identity.AddClaim(new Claim("auth_time", authTime.ToString()));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    ///     Minimal harness for <see cref="EndpointFilterInvocationContext"/>,
    ///     which is abstract and cannot be constructed directly.
    /// </summary>
    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterInvocationContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        public override HttpContext HttpContext { get; }

        public override IList<object?> Arguments { get; } = [];

        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }
}
