using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Api.Extensions;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Ordering.Application.Orders.Queries;
using NetCommerce.Ordering.Domain.Orders;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Ordering;

/// <summary>
///     Read side of the order API.
/// </summary>
/// <remarks>
/// <para>
///     Split into its own file rather than added to <c>OrderEndpoints</c>. That class
///     is the write side (create, cancel, ops), and this is a genuinely different
///     concern: it exists so the storefront can render an order page, which needs
///     the authoritative persisted state alongside the realtime stream.
/// </para>
/// <para>
///     Without it the storefront's REST reconciliation had no endpoint to call at
///     all, so a page could only ever show what the socket delivered — and the
///     socket is an accelerator, not the source of truth.
/// </para>
/// </remarks>
public static class OrderReadEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/orders")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Orders")
            .RequireRateLimiting("PerUser");

        // Deliberately separate from OrderEndpoints so the two maps of the same
        // group cannot silently disagree about version or rate-limit policy.
        group.MapGet("/{orderId:guid}", GetOrder)
            .WithName("GetOrder")
            .WithSummary("Get a single order")
            .WithDescription(
                "Returns the authoritative persisted state of an order. Used by the "
                + "storefront to reconcile against the realtime stream on load and reconnect.")
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization("CustomerOnly");
    }

    /// <summary>
    ///     Returns one order, provided the caller owns it.
    /// </summary>
    /// <remarks>
    /// PEAA Service Layer: presentation extracts the caller identity from
    /// transport and dispatches to the application read API. Repository access,
    /// ownership enforcement, and DTO translation live in the handler —
    /// this endpoint never touches persistence.
    /// </remarks>
    private static async Task<IResult> GetOrder(
        Guid orderId,
        IMessageBus bus,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Fail CLOSED. A non-Guid subject is forbidden, never treated as
        // "no owner" — otherwise a malformed or absent claim could return
        // another customer's order. Sound because CreateOrder stamps
        // CustomerId from this same claim. Chain owned by
        // ClaimsPrincipalExtensions (sub -> user_id -> NameIdentifier).
        if (!httpContext.User.TryGetCustomerId(out var callerCustomerId))
        {
            return Results.Forbid();
        }

        var result = await bus.InvokeAsync<Result<OrderDetailsDto>>(
            new GetOrderByIdQuery(orderId, callerCustomerId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return OrderLookupResults.FromFailure(orderId, result.Error, httpContext);
        }

        var order = result.Value;
        return Results.Ok(new OrderResponse(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.CreatedAt,
            order.PaidAt,
            order.ShippedAt,
            order.DeliveredAt,
            order.CancelledAt,
            order.CancellationReason));
    }
}

/// <summary>
///     An order as the storefront sees it.
/// </summary>
/// <param name="Id">Order identity.</param>
/// <param name="OrderNumber">Human-facing reference.</param>
/// <param name="Status">
///     The PERSISTED <c>OrderStatus</c> enum, serialized as its numeric value.
/// </param>
/// <param name="CreatedAt">When the order was submitted.</param>
/// <param name="PaidAt">When payment settled, or null.</param>
/// <param name="ShippedAt">When it shipped, or null.</param>
/// <param name="DeliveredAt">When it was delivered, or null.</param>
/// <param name="CancelledAt">When it was cancelled, or null.</param>
/// <param name="CancellationReason">Why it was cancelled, or null.</param>
/// <remarks>
/// <para>
///     Deliberately minimal: no addresses, no payment identifiers, no items. The
///     order page needs status and timeline, and every extra field is one more piece
///     of customer data exposed to a storefront render.
/// </para>
/// <para>
///     <c>Status</c> is the persisted enum, NOT the saga's free-form push strings.
///     Those are a different vocabulary — the storefront maps this one with
///     mapOrderStatus and the realtime stream with mapRealtimeStatus, and conflating
///     them is a bug this type is shaped to prevent.
/// </para>
/// </remarks>
public record OrderResponse(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime? PaidAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime? CancelledAt,
    string? CancellationReason);