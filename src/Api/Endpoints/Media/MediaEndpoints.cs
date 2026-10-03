#nullable enable
using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Media.Application.Services;

namespace NetCommerce.Api.Endpoints.Media;

// Strongly-typed AOT-safe DTOs (anonymous payloads crash the Native AOT
// source-generation resolver at runtime with "JsonTypeInfo metadata ... was
// not provided" — a 500 on the endpoint that returns them).
public sealed record UploadMediaResponse(string Key, string Url);
public sealed record MediaUploadError(string Error);
public sealed record PublicUrlResponse(string Url);

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

    /// <summary>
    ///     Uploads a file from a multipart/form-data body (field name "file").
    ///     The form is read manually from <see cref="HttpContext"/> rather than
    ///     binding an <c>IFormFile</c> parameter: the .NET OpenAPI schema
    ///     generator has no source-generation metadata for <c>IFormFile</c>,
    ///     so the parameter form 500'd the whole /openapi/v1.json document
    ///     (and broke the storefront's codegen:api type-regen workflow).
    /// </summary>
    private static async Task<IResult> Upload(
        HttpContext httpContext,
        IStorageService storageService,
        string folder = "products",
        CancellationToken cancellationToken = default)
    {
        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

        if (file is null || file.Length == 0)
            return Results.BadRequest(new MediaUploadError("File is required"));

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
        return Results.Ok(new PublicUrlResponse(url));
    }
}
