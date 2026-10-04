#nullable enable
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Kernel.Core.Results;

namespace NetCommerce.Api.Endpoints.Ordering;

/// <summary>
///     Single owner for the storefront order-lookup error contract
///     (refactoring: Consolidate Duplicate Conditional Fragments).
/// </summary>
/// <remarks>
/// <para>
///     Both the order read and the cancel pre-check map Service Layer lookup
///     failures to HTTP the same way: 404 carries an
///     <see cref="OrderMessageResponse"/> body the storefront renders, 403 is a
///     bare <c>Forbid</c> (no existence oracle), anything else follows the
///     RFC 9457 mapping. The if-chain lived copy-pasted in both endpoints —
///     any contract change needed two identical edits.
/// </para>
/// </remarks>
internal static class OrderLookupResults
{
    public static IResult FromFailure(Guid orderId, Error error, HttpContext? httpContext)
    {
        if (error.Code.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
            return Results.NotFound(new OrderMessageResponse(orderId, "Order not found."));
        if (error.Code.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
            return Results.Forbid();
        return error.ToHttpResult(httpContext);
    }
}
