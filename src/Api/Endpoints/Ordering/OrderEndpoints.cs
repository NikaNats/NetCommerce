using Microsoft.AspNetCore.Mvc;
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Api.Endpoints;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Results;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Ordering;

public class OrderEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders")
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
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderCommand command,
        IMessageBus bus,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<Guid>>(command, cancellationToken);

        if (!result.IsSuccess) return result.ToApiResult();

        var location = $"/api/v1/orders/{result.Value}";
        httpContext.Response.Headers.Location = location;

        return Results.Created(location, new { id = result.Value });
    }
}
