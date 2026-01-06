using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning.Builder;
using NetCommerce.Catalog.Application.Products.DTOs;
using NetCommerce.Catalog.Application.Products.Queries;
using NetCommerce.SharedKernel.Results;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Catalog.Products.GetProduct;

public sealed class GetProductEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        // ვქმნით ჯგუფს ვერსიონირებული როუტისთვის, რაც თავიდან აგვაცილებს ASP0018-ს
        var group = app.MapGroup("/api/v{version:apiVersion}/products")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Products");

        group.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetProductById")
            .WithSummary("Get a product by its unique identifier")
            .WithDescription("Retrieves detailed product information from the catalog.")
            // Produces<T> და TypedResults ავტომატურად აგენერირებს OpenAPI დოკუმენტაციას .WithOpenApi()-ს გარეშე
            .Produces<ProductDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .DisableAntiforgery();
    }

    public static async Task<Results<Ok<ProductDto>, NotFound<ProblemDetails>>> HandleAsync(
        Guid id,
        // ვამატებთ ვერსიას პარამეტრებში ანალიზატორის დასაკმაყოფილებლად (თუ როუტში გვიწერია)
        // [FromRoute] string version,
        IMessageBus bus,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ProductDto>>(new GetProductByIdQuery(id), ct);

        if (result.IsFailure)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Product Not Found",
                Detail = result.Error.Description,
                Status = StatusCodes.Status404NotFound
            });
        }

        return TypedResults.Ok(result.Value);
    }
}
