using Microsoft.AspNetCore.Mvc;
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.SharedKernel.Application;
using NetCommerce.SharedKernel.Results;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Catalog;

/// <summary>
///     RESTful endpoints for Product resources.
/// </summary>
public class ProductEndpoints : IEndpointGroup
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products")
            .WithTags("Products")
            .WithDescription("Manage product catalog resources");

        // GET /api/v1/products/{id} - Retrieve a single product
        group.MapGet("/{id:guid}", GetById)
            .WithName("GetProductById")
            .WithSummary("Get a product by its ID")
            .WithDescription("Retrieves a single product resource by its unique identifier.")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // GET /api/v1/products/slug/{slug} - Retrieve by slug (alternative identifier)
        group.MapGet("/slug/{slug}", GetBySlug)
            .WithName("GetProductBySlug")
            .WithSummary("Get a product by its URL-friendly slug")
            .WithDescription("Retrieves a single product resource using its SEO-friendly slug identifier.")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // GET /api/v1/products - Search/List products with pagination and filtering
        group.MapGet("/", Search)
            .WithName("SearchProducts")
            .WithSummary("Search and list products with pagination")
            .WithDescription(
                "Returns a paginated list of products. Supports filtering by category, price range, and full-text search.")
            .Produces<PaginatedResponse<object>>()
            .AllowAnonymous();

        // POST /api/v1/products - Create a new product
        group.MapPost("/", Create)
            .WithName("CreateProduct")
            .WithSummary("Create a new product")
            .WithDescription("Creates a new product resource. Returns 201 Created with Location header.")
            .Produces(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization("VendorOnly");

        // PUT /api/v1/products/{id} - Full update of a product
        group.MapPut("/{id:guid}", Update)
            .WithName("UpdateProduct")
            .WithSummary("Update an existing product")
            .WithDescription("Performs a full update of a product resource. All fields must be provided.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("VendorOnly");

        // PATCH /api/v1/products/{id}/price - Partial update (price only)
        group.MapPatch("/{id:guid}/price", UpdatePrice)
            .WithName("UpdateProductPrice")
            .WithSummary("Update product price")
            .WithDescription("Performs a partial update on the product's price. Uses JSON Merge Patch semantics.")
            .Accepts<UpdateProductPriceRequest>("application/merge-patch+json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("VendorOnly");

        // POST /api/v1/products/{id}/publish - Action endpoint (state transition)
        group.MapPost("/{id:guid}/publish", Publish)
            .WithName("PublishProduct")
            .WithSummary("Publish a product")
            .WithDescription("Transitions a product to published state, making it visible to customers.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("VendorOnly");

        // POST /api/v1/products/{id}/images - Add sub-resource
        group.MapPost("/{id:guid}/images", AddImage)
            .WithName("AddProductImage")
            .WithSummary("Add an image to a product")
            .WithDescription("Adds a new image to the product's image collection.")
            .Produces(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("VendorOnly");

        // DELETE /api/v1/products/{id} - Remove a product
        group.MapDelete("/{id:guid}", Delete)
            .WithName("DeleteProduct")
            .WithSummary("Delete a product")
            .WithDescription("Removes a product from the catalog. This action cannot be undone.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("VendorOnly");
    }

    private static async Task<IResult> GetById(
        Guid id,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetProductByIdQuery(id);
        var result = await bus.InvokeAsync<Result<ProductDto>>(query, cancellationToken);

        return result.ToApiResult();
    }

    private static async Task<IResult> GetBySlug(
        string slug,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetProductBySlugQuery(slug);
        var result = await bus.InvokeAsync<Result<ProductDto>>(query, cancellationToken);

        return result.ToApiResult();
    }

    private static async Task<IResult> Search(
        IMessageBus bus,
        [FromQuery] string? searchTerm = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = new SearchProductsQuery(searchTerm, categoryId, minPrice, maxPrice, page, pageSize);
        var result = await bus.InvokeAsync<Result<PagedResult<ProductDto>>>(query, cancellationToken);

        if (!result.IsSuccess) return result.ToApiResult();

        var paginatedResult = result.Value;
        var response = PaginatedResponse<object>.Create(
            paginatedResult!.Items.Cast<object>().ToList(),
            page,
            pageSize,
            paginatedResult.TotalCount);

        return Results.Ok(response);
    }

    private static async Task<IResult> Create(
        CreateProductCommand command,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<SharedKernel.Results.Result<Guid>>(command, cancellationToken);

        if (!result.IsSuccess) return result.ToApiResult();

        var location = $"/api/v1/products/{result.Value}";
        httpContext.Response.Headers.Location = location;

        return Results.Created(location, new { id = result.Value });
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateProductCommand command,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (id != command.ProductId)
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Product ID in URL does not match the request body.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

        var result = await bus.InvokeAsync<SharedKernel.Results.Result>(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> UpdatePrice(
        Guid id,
        UpdateProductPriceRequest request,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var command = new UpdateProductPriceCommand(id, request.Amount, request.Currency);
        var result = await bus.InvokeAsync<SharedKernel.Results.Result>(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> Publish(
        Guid id,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var command = new PublishProductCommand(id);
        var result = await bus.InvokeAsync<SharedKernel.Results.Result>(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> AddImage(
        Guid id,
        AddProductImageRequest request,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var command = new AddProductImageCommand(id, request.ImageKey, request.DisplayOrder, request.IsPrimary);
        var result = await bus.InvokeAsync<SharedKernel.Results.Result>(command, cancellationToken);

        if (!result.IsSuccess) return result.ToApiResult();

        // Return 201 Created with the product images location
        var location = $"/api/v1/products/{id}/images";
        return Results.Created(location, new { productId = id, imageKey = request.ImageKey });
    }

    private static async Task<IResult> Delete(
        Guid id,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        // Using ArchiveProductCommand for soft delete (RESTful best practice)
        var command = new ArchiveProductCommand(id);
        var result = await bus.InvokeAsync<SharedKernel.Results.Result>(command, cancellationToken);
        return result.ToApiResult();
    }
}

/// <summary>
///     Request model for updating product price (JSON Merge Patch).
/// </summary>
/// <param name="Amount">The new price amount.</param>
/// <param name="Currency">The currency code (e.g., "USD", "EUR").</param>
public record UpdateProductPriceRequest(decimal Amount, string Currency);

/// <summary>
///     Request model for adding a product image.
/// </summary>
/// <param name="ImageKey">The storage key for the image.</param>
/// <param name="DisplayOrder">The display order (lower numbers shown first).</param>
/// <param name="IsPrimary">Whether this is the primary product image.</param>
public record AddProductImageRequest(string ImageKey, int DisplayOrder, bool IsPrimary);
