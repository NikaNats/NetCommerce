#nullable enable
using System.Security.Claims;
using Asp.Versioning.Builder; // Required for ApiVersionSet
using NetCommerce.Basket.Application;

namespace NetCommerce.Api.Endpoints.Basket;

public class BasketEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/basket")
            .WithApiVersionSet(versionSet) // <--- THIS IS CRITICAL
            .HasApiVersion(1.0)            // Specify which versions this group supports
            .WithTags("Basket")
            .RequireAuthorization();

        group.MapGet("/", GetBasket)
            .WithName("GetBasket")
            .WithSummary("Get current user's basket");

        group.MapPost("/items", AddItem)
            .WithName("AddBasketItem")
            .WithSummary("Add item to basket");

        group.MapPut("/items/{productId:guid}", UpdateItemQuantity)
            .WithName("UpdateBasketItemQuantity")
            .WithSummary("Update item quantity");

        group.MapDelete("/items/{productId:guid}", RemoveItem)
            .WithName("RemoveBasketItem")
            .WithSummary("Remove item from basket");

        group.MapDelete("/", ClearBasket)
            .WithName("ClearBasket")
            .WithSummary("Clear basket");
    }

    private static string GetCustomerId(HttpContext context)
    {
        return context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? throw new UnauthorizedAccessException("User not authenticated");
    }

    private static async Task<IResult> GetBasket(
        HttpContext context,
        IBasketRepository basketRepository,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId(context);
        var basket = await basketRepository.GetBasketAsync(customerId, cancellationToken)
                     ?? ShoppingBasket.Create(customerId);
        return Results.Ok(basket);
    }

    private static async Task<IResult> AddItem(
        AddBasketItemRequest request,
        HttpContext context,
        IBasketRepository basketRepository,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId(context);
        var basket = await basketRepository.GetBasketAsync(customerId, cancellationToken)
                     ?? ShoppingBasket.Create(customerId);

        var item = new BasketItem
        {
            ProductId = request.ProductId,
            ProductName = request.ProductName,
            Sku = request.Sku,
            Quantity = request.Quantity,
            Price = request.UnitPrice,
            ImageUrl = request.ImageUrl
        };

        basket.AddItem(item);
        await basketRepository.UpdateBasketAsync(basket, cancellationToken);
        return Results.Ok(basket);
    }

    private static async Task<IResult> UpdateItemQuantity(
        Guid productId,
        UpdateQuantityRequest request,
        HttpContext context,
        IBasketRepository basketRepository,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId(context);
        var basket = await basketRepository.GetBasketAsync(customerId, cancellationToken);

        if (basket == null)
            return Results.NotFound("Basket not found");

        basket.UpdateItemQuantity(productId, request.Quantity);
        await basketRepository.UpdateBasketAsync(basket, cancellationToken);
        return Results.Ok(basket);
    }

    private static async Task<IResult> RemoveItem(
        Guid productId,
        HttpContext context,
        IBasketRepository basketRepository,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId(context);
        var basket = await basketRepository.GetBasketAsync(customerId, cancellationToken);

        if (basket == null)
            return Results.NotFound("Basket not found");

        basket.RemoveItem(productId);
        await basketRepository.UpdateBasketAsync(basket, cancellationToken);
        return Results.Ok(basket);
    }

    private static async Task<IResult> ClearBasket(
        HttpContext context,
        IBasketRepository basketRepository,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId(context);
        var success = await basketRepository.DeleteBasketAsync(customerId, cancellationToken);
        return success ? Results.NoContent() : Results.BadRequest("Failed to clear basket");
    }
}

public record AddBasketItemRequest(
    Guid ProductId,
    string ProductName,
    string? Sku,
    int Quantity,
    decimal UnitPrice,
    string? ImageUrl);

public record UpdateQuantityRequest(int Quantity);
