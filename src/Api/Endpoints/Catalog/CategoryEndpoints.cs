using MediatR;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Api.Endpoints.Common;
using NetCommerce.Catalog.Application.Categories.Commands;
using NetCommerce.Catalog.Application.Categories.Queries;

namespace NetCommerce.Api.Endpoints.Catalog;

/// <summary>
/// RESTful endpoints for Category resources.
/// Follows best practices: nouns for resources, proper HTTP methods, and HATEOAS links.
/// </summary>
public class CategoryEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/categories")
            .WithTags("Categories")
            .WithDescription("Manage product category resources");

        // GET /api/v1/categories - List all categories
        group.MapGet("/", GetAll)
            .WithName("GetAllCategories")
            .WithSummary("Get all categories")
            .WithDescription("Retrieves a hierarchical list of all product categories.")
            .Produces<CollectionResponse<CategoryResponse>>(StatusCodes.Status200OK)
            .AllowAnonymous();

        // GET /api/v1/categories/{id} - Get category by ID
        group.MapGet("/{id:guid}", GetById)
            .WithName("GetCategoryById")
            .WithSummary("Get a category by ID")
            .WithDescription("Retrieves a single category resource by its unique identifier.")
            .Produces<ResourceResponse<CategoryResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // GET /api/v1/categories/slug/{slug} - Get category by slug
        group.MapGet("/slug/{slug}", GetBySlug)
            .WithName("GetCategoryBySlug")
            .WithSummary("Get a category by slug")
            .WithDescription("Retrieves a single category resource using its SEO-friendly slug identifier.")
            .Produces<ResourceResponse<CategoryResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // GET /api/v1/categories/{id}/children - Get child categories
        group.MapGet("/{id:guid}/children", GetChildren)
            .WithName("GetChildCategories")
            .WithSummary("Get child categories")
            .WithDescription("Retrieves all child categories of the specified parent category.")
            .Produces<CollectionResponse<CategoryResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // POST /api/v1/categories - Create a new category
        group.MapPost("/", Create)
            .WithName("CreateCategory")
            .WithSummary("Create a new category")
            .WithDescription("Creates a new product category. Returns 201 Created with Location header.")
            .Produces<CategoryResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization("VendorOnly");

        // PUT /api/v1/categories/{id} - Update a category
        group.MapPut("/{id:guid}", Update)
            .WithName("UpdateCategory")
            .WithSummary("Update a category")
            .WithDescription("Performs a full update of a category resource.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("VendorOnly");

        // DELETE /api/v1/categories/{id} - Delete a category
        group.MapDelete("/{id:guid}", Delete)
            .WithName("DeleteCategory")
            .WithSummary("Delete a category")
            .WithDescription("Removes a category. Fails if category contains products.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("VendorOnly");
    }

    private static async Task<IResult> GetAll(
        HttpContext httpContext,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetAllCategoriesQuery();
        var result = await mediator.Send(query, cancellationToken);
        
        if (!result.IsSuccess)
        {
            return result.ToApiResult();
        }

        var selfUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/v1/categories";
        var response = CollectionResponse<object>.Create(
            result.Value!.Cast<object>().ToList(),
            selfUrl);

        return Results.Ok(response);
    }

    private static async Task<IResult> GetById(
        Guid id,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetCategoryByIdQuery(id);
        var result = await mediator.Send(query, cancellationToken);
        
        var selfUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/v1/categories/{id}";
        return result.ToResourceResult(selfUrl,
            new Link("children", $"/api/v1/categories/{id}/children", "GET"),
            new Link("products", $"/api/v1/products?categoryId={id}", "GET"));
    }

    private static async Task<IResult> GetBySlug(
        string slug,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetCategoryBySlugQuery(slug);
        var result = await mediator.Send(query, cancellationToken);
        
        var selfUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/v1/categories/slug/{slug}";
        return result.ToResourceResult(selfUrl);
    }

    private static async Task<IResult> GetChildren(
        Guid id,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var query = new GetChildCategoriesQuery(id);
        var result = await mediator.Send(query, cancellationToken);
        
        if (!result.IsSuccess)
        {
            return result.ToApiResult();
        }

        var selfUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/v1/categories/{id}/children";
        var response = CollectionResponse<object>.Create(
            result.Value!.Cast<object>().ToList(),
            selfUrl,
            new Link("parent", $"/api/v1/categories/{id}", "GET"));

        return Results.Ok(response);
    }

    private static async Task<IResult> Create(
        CreateCategoryCommand command,
        HttpContext httpContext,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        
        if (!result.IsSuccess)
        {
            return result.ToApiResult();
        }

        var location = $"/api/v1/categories/{result.Value}";
        return Results.Created(location, new { id = result.Value });
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateCategoryCommand command,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        if (id != command.CategoryId)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Category ID in URL does not match the request body.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var result = await mediator.Send(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> Delete(
        Guid id,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        var command = new DeleteCategoryCommand(id);
        var result = await mediator.Send(command, cancellationToken);
        return result.ToApiResult();
    }
}

/// <summary>
/// Category response model (placeholder - actual implementation depends on query handlers).
/// </summary>
public record CategoryResponse;
