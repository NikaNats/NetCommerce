#nullable enable
using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Media.Application.Services;

namespace NetCommerce.Api.Endpoints.Media;

// Strongly-typed AOT-safe DTO (fixes runtime serialization crashes in Native AOT)
public sealed record UploadMediaResponse(string Key, string Url);

public class MediaEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/media")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Media");

        group.MapGet("/upload-url", GetUploadUrl)
            .WithName("GetMediaUploadUrl")
            .RequireAuthorization("VendorOnly");

        group.MapPost("/upload", Upload)
            .WithName("UploadMedia")
            .RequireAuthorization("VendorOnly")
            .DisableAntiforgery();

        group.MapDelete("/", Delete)
            .WithName("DeleteMedia")
            .RequireAuthorization("VendorOnly");

        group.MapGet("/url", GetPublicUrl)
            .WithName("GetMediaPublicUrl")
            .AllowAnonymous();
    }

    private static async Task<IResult> Upload(
        IFormFile? file,
        IStorageService storageService,
        HttpContext httpContext,
        string folder = "products",
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "File is required" });

        await using var stream = file.OpenReadStream();

        var result = await storageService.UploadAsync(
            stream,
            file.FileName,
            file.ContentType,
            folder,
            cancellationToken);

        if (result.IsSuccess)
        {
            var version = httpContext.Features.Get<IApiVersioningFeature>()?.RequestedApiVersion ?? new ApiVersion(1, 0);
            var location = $"/api/v{version.MajorVersion}/media/{result.Value}";

            // Using strongly-typed record for AOT safety
            return Results.Created(location, new UploadMediaResponse(
                result.Value!,
                storageService.GetPublicUrl(result.Value!)
            ));
        }

        return result.ToApiResult();
    }

    private static async Task<IResult> GetUploadUrl(
        string fileName,
        string contentType,
        IStorageService storageService,
        string folder = "products",
        int expiryMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var result = await storageService.GetPresignedUploadUrlAsync(
            folder,
            fileName,
            contentType,
            TimeSpan.FromMinutes(expiryMinutes),
            cancellationToken);

        return result.ToApiResult();
    }

    private static async Task<IResult> Delete(
        string key,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        var result = await storageService.DeleteAsync(key, cancellationToken);
        return result.ToApiResult();
    }

    private static IResult GetPublicUrl(
        string key,
        IStorageService storageService)
    {
        var url = storageService.GetPublicUrl(key);
        return Results.Ok(new { Url = url });
    }
}
