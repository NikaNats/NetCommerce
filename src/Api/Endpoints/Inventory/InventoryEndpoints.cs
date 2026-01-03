using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Application.Stock.Queries;
using NetCommerce.SharedKernel.Results;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Inventory;

public class InventoryEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory")
            .WithTags("Inventory");

        group.MapGet("/product/{productId:guid}", GetByProductId)
            .WithName("GetStockByProductId")
            .WithSummary("Get stock by product ID")
            .AllowAnonymous();

        group.MapGet("/low-stock", GetLowStockItems)
            .WithName("GetLowStockItems")
            .WithSummary("Get low stock items")
            .RequireAuthorization("VendorOnly");

        group.MapPost("/", CreateStock)
            .WithName("CreateStock")
            .WithSummary("Create stock record for a product")
            .RequireAuthorization("VendorOnly");

        group.MapPatch("/{stockId:guid}/quantity", UpdateQuantity)
            .WithName("UpdateStockQuantity")
            .WithSummary("Update stock quantity")
            .RequireAuthorization("VendorOnly");

        group.MapPost("/reserve", ReserveStock)
            .WithName("ReserveStock")
            .WithSummary("Reserve stock for an order (15-minute hold)")
            .RequireAuthorization();

        group.MapPost("/products/{productId:guid}/reservations/{reservationId:guid}/confirm", ConfirmReservation)
            .WithName("ConfirmStockReservation")
            .WithSummary("Confirm a stock reservation")
            .RequireAuthorization();

        group.MapPost("/products/{productId:guid}/reservations/{reservationId:guid}/release", ReleaseReservation)
            .WithName("ReleaseStockReservation")
            .WithSummary("Release a stock reservation")
            .RequireAuthorization();
    }

    private static async Task<IResult> GetByProductId(
        Guid productId,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetStockByProductIdQuery(productId);
        var result = await bus.InvokeAsync<Result<StockDto>>(query, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> GetLowStockItems(
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetLowStockItemsQuery();
        var result = await bus.InvokeAsync<Result<IReadOnlyList<StockDto>>>(query, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> CreateStock(
        CreateStockCommand command,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<Guid>>(command, cancellationToken);
        return result.ToCreatedResult();
    }

    private static async Task<IResult> UpdateQuantity(
        Guid stockId,
        UpdateStockQuantityRequest request,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var command = new UpdateStockQuantityCommand(stockId, request.QuantityDelta, request.Reason);
        var result = await bus.InvokeAsync<Result>(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> ReserveStock(
        ReserveStockCommand command,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<Guid>>(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> ConfirmReservation(
        Guid productId,
        Guid reservationId,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var command = new ConfirmReservationCommand(productId, reservationId);
        var result = await bus.InvokeAsync<Result>(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> ReleaseReservation(
        Guid productId,
        Guid reservationId,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var command = new ReleaseReservationCommand(productId, reservationId);
        var result = await bus.InvokeAsync<Result>(command, cancellationToken);
        return result.ToApiResult();
    }
}

public record UpdateStockQuantityRequest(int QuantityDelta, string Reason);