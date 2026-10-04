#nullable enable
using Asp.Versioning.Builder; // Required for ApiVersionSet
using NetCommerce.Api.Extensions;
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
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

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
        // Chain owned by ClaimsPrincipalExtensions (sub -> user_id ->
        // NameIdentifier). Basket keys are opaque strings, so unlike ordering
        // this additionally accepts preferred_username for tokens issued by
        // other providers.
        // NOTE: Must throw UnauthorizedAccessException (not BadHttpRequestException):
        // GlobalExceptionHandler maps it to 401, anything else becomes 500.
        var customerId = context.User.TryGetSubjectId()
            ?? context.User.FindFirst("preferred_username")?.Value;

        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new UnauthorizedAccessException(
                "User token lacks a valid subject ('sub') claim.");
        }

        return customerId;
    }

    private static async Task<IResult> GetBasket(
        HttpContext context,
        IBasketRepository basketRepository,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId(context);
        var basket = await basketRepository.GetBasketAsync(customerId, cancellationToken)
                     ?? ShoppingBasket.Create(customerId);
        // PEAA Remote Facade: never leak the domain/persistence model across
        // the remote boundary — translate to the transport DTO.
        return Results.Ok(BasketMapper.ToDto(basket));
    }

    private static async Task<IResult> AddItem(
        AddBasketItemRequest request,
        HttpContext context,
        IBasketRepository basketRepository,
        IProductPriceSource priceSource,
        CancellationToken cancellationToken)
    {
        var customerId = GetCustomerId(context);
        var basket = await basketRepository.GetBasketAsync(customerId, cancellationToken)
                     ?? ShoppingBasket.Create(customerId);

        // Price, name, SKU and image come from the CATALOG, never the request body.
        //
        // This previously read `Price = request.UnitPrice`, so any caller could add a
        // product at an arbitrary price and have the Redis-persisted total reflect it --
        // a revenue-path integrity defect, not a display concern. BasketPricer takes
        // no price parameter at all, so the field cannot be supplied wrongly.
        BasketItem item;
        try
        {
            item = await BasketPricer.CreateLineAsync(
                priceSource, request.ProductId, request.Quantity, cancellationToken);
        }
        catch (BasketPricer.ProductNotSellableException)
        {
            // 404 rather than 400: an unpublished product must not be
            // distinguishable from a nonexistent one by a storefront crawler.
            return Results.NotFound();
        }

        basket.AddItem(item);
        return await SaveAndRespondAsync(basket, basketRepository, cancellationToken);
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
        return await SaveAndRespondAsync(basket, basketRepository, cancellationToken);
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
        return await SaveAndRespondAsync(basket, basketRepository, cancellationToken);
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

    /// <summary>
    ///     Persist a mutated basket and translate it to the transport contract
    ///     (refactoring: Extract Method over the 3x duplicated persist+map tail).
    /// </summary>
    private static async Task<IResult> SaveAndRespondAsync(
        ShoppingBasket basket,
        IBasketRepository basketRepository,
        CancellationToken cancellationToken)
    {
        await basketRepository.UpdateBasketAsync(basket, cancellationToken);
        // PEAA Remote Facade: never leak the domain/persistence model across
        // the remote boundary — translate to the transport DTO.
        return Results.Ok(BasketMapper.ToDto(basket));
    }
}

/// <param name="ProductId">The only field that identifies WHAT is being added.</param>
/// <param name="Quantity">How many. Validated, and positive.</param>
/// <remarks>
/// ProductName, Sku, UnitPrice and ImageUrl were REMOVED rather than merely ignored.
/// They were client-supplied, and UnitPrice in particular let a caller set their own
/// price, which the endpoint then persisted into the basket total. Those values are
/// now resolved from the catalog. Leaving the fields on the contract would be a lie
/// to the next reader -- it would look as though the values were honoured.
/// </remarks>
public record AddBasketItemRequest(
    Guid ProductId,
    int Quantity);

public record UpdateQuantityRequest(int Quantity);
