#nullable enable
using Microsoft.AspNetCore.Authorization;

namespace NetCommerce.Kernel.Security.Authorization;

/// <summary>
///     Requirement for resource ownership authorization.
///     Ensures users can only access resources they own (e.g., "can only view own orders").
///     Usage: .RequireAuthorization("OwnerOnly")
/// </summary>
public sealed class ResourceOwnerRequirement : IAuthorizationRequirement
{
    /// <summary>
    ///     The claim type that contains the owner/user identifier.
    ///     Defaults to "sub" (standard OIDC subject claim).
    /// </summary>
    public string OwnerClaimType { get; init; } = "sub";
}

/// <summary>
///     Marker interface for resources that have an owner.
///     Implement this on domain entities or DTOs that participate
///     in resource-based authorization.
/// </summary>
public interface IOwnedResource
{
    /// <summary>
    ///     The user ID of the resource owner.
    /// </summary>
    string OwnerId { get; }
}

/// <summary>
///     Authorization handler that validates the current user is the resource owner.
///     Supports both IOwnedResource and direct string comparison via route values.
///     Admins bypass ownership checks (defense in depth: admin role is still required on the endpoint).
/// </summary>
public sealed class ResourceOwnerAuthorizationHandler
    : AuthorizationHandler<ResourceOwnerRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement,
        IOwnedResource resource)
    {
        // Admins bypass ownership checks
        if (context.User.IsInRole("admin"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirst(requirement.OwnerClaimType)?.Value
                     ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId) &&
            string.Equals(userId, resource.OwnerId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Endpoint filter that enforces the current user can only access their own resources.
///     Extracts user ID from claims and compares against the {userId} or {customerId} route parameter.
///     This is applied at the endpoint level for Minimal APIs as an IEndpointFilter.
/// </summary>
public sealed class OwnerOnlyEndpointFilter : Microsoft.AspNetCore.Http.IEndpointFilter
{
    private readonly string _routeParamName;

    /// <param name="routeParamName">
    ///     The route parameter name containing the resource owner's ID (default: "userId").
    /// </param>
    public OwnerOnlyEndpointFilter(string routeParamName = "userId")
    {
        _routeParamName = routeParamName;
    }

    public async ValueTask<object?> InvokeAsync(
        Microsoft.AspNetCore.Http.EndpointFilterInvocationContext context,
        Microsoft.AspNetCore.Http.EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Admins bypass
        if (httpContext.User.IsInRole("admin"))
            return await next(context);

        var userId = httpContext.User.FindFirst("sub")?.Value
                     ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Microsoft.AspNetCore.Http.Results.Problem(
                title: "Unauthorized",
                detail: "User identity could not be determined.",
                statusCode: 401,
                type: "https://docs.netcommerce.io/errors/unauthorized");
        }

        // Check route parameter
        var routeValue = httpContext.Request.RouteValues[_routeParamName]?.ToString();
        if (!string.IsNullOrEmpty(routeValue) &&
            !string.Equals(userId, routeValue, StringComparison.OrdinalIgnoreCase))
        {
            return Microsoft.AspNetCore.Http.Results.Problem(
                title: "Forbidden",
                detail: "You can only access your own resources.",
                statusCode: 403,
                type: "https://docs.netcommerce.io/errors/resource-owner-mismatch");
        }

        return await next(context);
    }
}
