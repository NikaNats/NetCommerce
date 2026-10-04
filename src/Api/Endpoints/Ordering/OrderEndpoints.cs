using Asp.Versioning;
using Asp.Versioning.Builder; // Required for ApiVersionSet
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Api.Extensions;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Orders.Queries;
using NetCommerce.Domain.Shared;
using NetCommerce.Kernel.Core.Results;
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
        // Chain owned by ClaimsPrincipalExtensions.
        if (!httpContext.User.TryGetCustomerId(out var customerId))
        {
            return Results.Unauthorized();
        }

        command = command with { CustomerId = customerId };

        var result = await bus.InvokeAsync<Result<Guid>>(command, cancellationToken);

        if (!result.IsSuccess) return result.ToApiResult();

        var version = httpContext.Features.Get<Asp.Versioning.IApiVersioningFeature>()?.RequestedApiVersion ?? new ApiVersion(1, 0);
        var location = $"/api/v{version.MajorVersion}/orders/{result.Value}";
        httpContext.Response.Headers.Location = location;

        return Results.Created(location, new CreatedResponse(result.Value));
    }

    private static async Task<IResult> GetStuckSagas(
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        // PEAA Service Layer + Gateway: saga state lives in Wolverine's own
        // store table (an external resource). The read goes through the
        // application query and its gateway — never raw SQL in presentation.
        var result = await bus.InvokeAsync<Result<IReadOnlyList<StuckSagaInfoDto>>>(
            new GetStuckSagasQuery(),
            cancellationToken);

        if (!result.IsSuccess)
            return result.Error.ToHttpResult();

        var dtos = result.Value
            .Select(s => new StuckSagaDto(
                s.OrderId,
                s.OrderNumber,
                s.PaymentTransactionId,
                s.RefundFailureReason,
                s.StuckSince,
                Money.Create(s.Amount, s.Currency)))
            .ToList();

        return Results.Ok(new StuckSagasResponse(
            dtos.Count,
            dtos));
    }

    private static async Task<IResult> CancelOrder(
        Guid orderId,
        string? reason,
        IMessageBus bus,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Ownership check stays at the edge (transport identity), but the owner
        // lookup itself goes through the Service Layer — no DbContext in
        // presentation. Fail closed: non-Guid subject or mismatch is forbidden.
        // Chain owned by ClaimsPrincipalExtensions (sub -> user_id ->
        // NameIdentifier): previously user_id was missing here, so callers on
        // the deployment's own token shape could create but never cancel.
        if (!httpContext.User.TryGetCustomerId(out var callerCustomerId))
        {
            return Results.Forbid();
        }

        var ownerResult = await bus.InvokeAsync<Result<Guid>>(
            new GetOrderOwnerQuery(orderId),
            cancellationToken);

        if (!ownerResult.IsSuccess)
        {
            return OrderLookupResults.FromFailure(orderId, ownerResult.Error, httpContext);
        }

        if (ownerResult.Value != callerCustomerId)
        {
            return Results.Forbid();
        }

        var result = await bus.InvokeAsync<Result>(
            new CancelOrderCommand(orderId, reason ?? "Cancelled by customer."),
            cancellationToken);

        if (!result.IsSuccess)
            return result.ToApiResult();

        return Results.Ok(new CancelOrderResponse(orderId, "Order cancelled."));
    }
}

public sealed record StuckSagasResponse(
    int Count,
    List<StuckSagaDto> Sagas);

public sealed record CancelOrderResponse(Guid Id, string Message);

public sealed record StuckSagaDto(
    Guid OrderId,
    string OrderNumber,
    string PaymentTransactionId,
    string RefundFailureReason,
    DateTime StuckSince,
    Money Amount);
