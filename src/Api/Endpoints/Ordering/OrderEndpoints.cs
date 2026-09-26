using Asp.Versioning;
using Asp.Versioning.Builder; // Required for ApiVersionSet
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.Core.Results;
using System.Security.Claims;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Ordering;

public class OrderEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/orders")
            .WithApiVersionSet(versionSet) // <--- THIS IS CRITICAL
            .HasApiVersion(1.0)            // Specify which versions this group supports
            .WithTags("Orders")
            .WithDescription("Submit and manage orders")
            .RequireRateLimiting("PerUser");

        group.MapPost("/", CreateOrder)
            .WithName("CreateOrder")
            .WithSummary("Create a new order")
            .WithDescription("Creates a new order and returns the order identifier.")
            .Produces(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<IdempotencyFilter>()
            .RequireAuthorization("CustomerOnly");

        group.MapGet("/manual-intervention", GetStuckSagas)
            .WithName("GetStuckSagas")
            .WithSummary("Get orders requiring manual intervention")
            .WithDescription("Returns all sagas in ManualInterventionRequired state (refund failed, requires ops team review).")
            .Produces<StuckSagasResponse>(StatusCodes.Status200OK)
            .RequireAuthorization("AdminOnly");

        group.MapDelete("/{orderId:guid}", CancelOrder)
            .WithName("CancelOrder")
            .WithSummary("Cancel an order")
            .WithDescription("Cancels the order and stops the running fulfillment saga (releases inventory, refunds if already paid). Only the owning customer may cancel.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .RequireAuthorization("CustomerOnly");
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderCommand command,
        IMessageBus bus,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Identity attribution: the order's customer ALWAYS comes from the
        // authenticated JWT subject — never from the request body. Accepting a
        // client-supplied CustomerId would let any customer create (and, via
        // idempotency scoping, read) orders under another customer's identity.
        var subject = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(subject, out var customerId))
        {
            return Results.Unauthorized();
        }

        command = command with { CustomerId = customerId };

        var result = await bus.InvokeAsync<Result<Guid>>(command, cancellationToken);

        if (!result.IsSuccess) return result.ToApiResult();

        var version = httpContext.Features.Get<Asp.Versioning.IApiVersioningFeature>()?.RequestedApiVersion ?? new ApiVersion(1, 0);
        var location = $"/api/v{version.MajorVersion}/orders/{result.Value}";
        httpContext.Response.Headers.Location = location;

        return Results.Created(location, new { id = result.Value });
    }

    private static async Task<IResult> GetStuckSagas(
        OrderingDbContext db,
        CancellationToken cancellationToken)
    {
        var stuckSagas = await db.Set<OrderFulfillmentSaga>()
            .AsNoTracking()
            .Where(s => s.State == OrderFulfillmentState.ManualInterventionRequired)
            .OrderBy(s => s.StartedAt)
            .Select(s => new StuckSagaDto(
                s.Id,
                s.OrderNumber,
                s.PaymentTransactionId ?? "N/A",
                s.FailureReason ?? "Unknown reason",
                s.StartedAt,
                s.TotalAmount))
            .ToListAsync(cancellationToken);

        return Results.Ok(new StuckSagasResponse(
            stuckSagas.Count,
            stuckSagas));
    }

    private static async Task<IResult> CancelOrder(
        Guid orderId,
        string? reason,
        OrderingDbContext db,
        IMessageBus bus,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Select(o => new { o.Id, o.CustomerId })
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
            return Results.NotFound(new { OrderId = orderId, Message = "Order not found." });

        // Ownership check: the JWT subject must identify the ordering customer.
        // Fail closed — a non-Guid subject (or a mismatch) is forbidden, never
        // treated as "no owner". This is sound because CreateOrder stamps
        // CustomerId from the same JWT subject claim.
        var subject = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(subject, out var callerCustomerId)
            || callerCustomerId != order.CustomerId)
        {
            return Results.Forbid();
        }

        var result = await bus.InvokeAsync<Result>(
            new CancelOrderCommand(orderId, reason ?? "Cancelled by customer."),
            cancellationToken);

        if (!result.IsSuccess)
            return result.ToApiResult();

        return Results.Ok(new { Id = orderId, Message = "Order cancelled." });
    }
}

public sealed record StuckSagasResponse(
    int Count,
    List<StuckSagaDto> Sagas);

public sealed record StuckSagaDto(
    Guid OrderId,
    string OrderNumber,
    string PaymentTransactionId,
    string RefundFailureReason,
    DateTime StuckSince,
    Money Amount);
