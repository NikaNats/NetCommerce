using Asp.Versioning;
using Asp.Versioning.Builder; // Required for ApiVersionSet
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Catalog.Application.Categories.Commands;
using NetCommerce.Catalog.Application.Categories.DTOs;
using NetCommerce.Catalog.Application.Categories.Queries;
using NetCommerce.Kernel.Core.Results;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Catalog;

/// <summary>
///     RESTful endpoints for Category resources.
/// </summary>
public class CategoryEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/categories")
            .WithApiVersionSet(versionSet) // <--- THIS IS CRITICAL
            .HasApiVersion(1.0)            // Specify which versions this group supports
            .WithTags("Categories")
            .WithDescription("Manage product category resources");

        // GET /api/v{version:apiVersion}/categories - List all categories
        group.MapGet("/", GetAll)
            .WithName("GetAllCategories")
            .WithSummary("Get all categories")
            .WithDescription("Retrieves a hierarchical list of all product categories.")
            .AllowAnonymous();

        // GET /api/v{version:apiVersion}/categories/{id} - Get category by ID
        group.MapGet("/{id:guid}", GetById)
            .WithName("GetCategoryById")
            .WithSummary("Get a category by ID")
            .WithDescription("Retrieves a single category resource by its unique identifier.")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // GET /api/v{version:apiVersion}/categories/slug/{slug} - Get category by slug
        group.MapGet("/slug/{slug}", GetBySlug)
            .WithName("GetCategoryBySlug")
            .WithSummary("Get a category by slug")
            .WithDescription("Retrieves a single category resource using its SEO-friendly slug identifier.")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // GET /api/v{version:apiVersion}/categories/{id}/children - Get child categories
        group.MapGet("/{id:guid}/children", GetChildren)
            .WithName("GetChildCategories")
            .WithSummary("Get child categories")
            .WithDescription("Retrieves all child categories of the specified parent category.")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // POST /api/v{version:apiVersion}/categories - Create a new category
        group.MapPost("/", Create)
            .WithName("CreateCategory")
            .WithSummary("Create a new category")
            .WithDescription("Creates a new product category. Returns 201 Created with Location header.")
            .Produces(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization("VendorOnly");

        // PUT /api/v{version:apiVersion}/categories/{id} - Update a category
        group.MapPut("/{id:guid}", Update)
            .WithName("UpdateCategory")
            .WithSummary("Update a category")
            .WithDescription("Performs a full update of a category resource.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("VendorOnly");

        // DELETE /api/v{version:apiVersion}/categories/{id} - Delete a category
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
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetAllCategoriesQuery();
        var result = await bus.InvokeAsync<Result<IReadOnlyList<CategoryDto>>>(query, cancellationToken);

        return result.ToApiResult();
    }

    private static async Task<IResult> GetById(
        Guid id,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetCategoryByIdQuery(id);
        var result = await bus.InvokeAsync<Result<CategoryDto>>(query, cancellationToken);

        return result.ToApiResult();
    }

    private static async Task<IResult> GetBySlug(
        string slug,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetCategoryBySlugQuery(slug);
        var result = await bus.InvokeAsync<Result<CategoryDto>>(query, cancellationToken);

        return result.ToApiResult();
    }

    private static async Task<IResult> GetChildren(
        Guid id,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var query = new GetChildCategoriesQuery(id);
        var result = await bus.InvokeAsync<Result<IReadOnlyList<CategoryDto>>>(query, cancellationToken);

        return result.ToApiResult();
    }

    private static async Task<IResult> Create(
        CreateCategoryCommand command,
        IMessageBus bus,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await bus.InvokeAsync<Result<Guid>>(command, cancellationToken);

        if (!result.IsSuccess) return result.ToApiResult();

        var version = httpContext.Features.Get<Asp.Versioning.IApiVersioningFeature>()?.RequestedApiVersion ?? new ApiVersion(1, 0);
        var location = $"/api/v{version.MajorVersion}/categories/{result.Value}";
        return Results.Created(location, new { id = result.Value });
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateCategoryCommand command,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (id != command.CategoryId)
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Category ID in URL does not match the request body.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

        var result = await bus.InvokeAsync<Result>(command, cancellationToken);
        return result.ToApiResult();
    }

    private static async Task<IResult> Delete(
        Guid id,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var command = new DeleteCategoryCommand(id);
        var result = await bus.InvokeAsync<Result>(command, cancellationToken);
        return result.ToApiResult();
    }
}
