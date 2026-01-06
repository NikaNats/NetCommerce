using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Api.Endpoints;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Results;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Ordering;

public class OrderEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/orders")
            .WithTags("Orders")
            .WithDescription("Submit and manage orders");

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
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderCommand command,
        IMessageBus bus,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
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
