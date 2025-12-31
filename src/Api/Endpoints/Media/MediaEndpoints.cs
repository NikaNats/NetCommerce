using NetCommerce.Api.Middleware;
using NetCommerce.Media.Application.Services;

namespace NetCommerce.Api.Endpoints.Media;

public class MediaEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/media")
            .WithTags("Media");

        group.MapGet("/upload-url", GetUploadUrl)
            .WithName("GetMediaUploadUrl")
            .WithSummary("Get a presigned URL for uploading a file")
            .RequireAuthorization("VendorOnly");

        group.MapPost("/upload", Upload)
            .WithName("UploadMedia")
            .WithSummary("Upload a file directly (for smaller files)")
            .RequireAuthorization("VendorOnly")
            .DisableAntiforgery()
            .WithIdempotency();

        group.MapDelete("/", Delete)
            .WithName("DeleteMedia")
            .WithSummary("Delete a file")
            .RequireAuthorization("VendorOnly");

        group.MapGet("/url", GetPublicUrl)
            .WithName("GetMediaPublicUrl")
            .WithSummary("Get public URL for an image")
            .AllowAnonymous();
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

    private static async Task<IResult> Upload(
        IFormFile file,
        IStorageService storageService,
        string folder = "products",
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest("File is required");

        if (file.Length > 10 * 1024 * 1024) // 10MB
            return Results.BadRequest("File size exceeds 10MB limit");

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return Results.BadRequest("Invalid file type. Allowed: jpeg, png, webp, gif");

        await using var stream = file.OpenReadStream();
        var result = await storageService.UploadAsync(
            stream,
            file.FileName,
            file.ContentType,
            folder,
            cancellationToken);

        if (result.IsSuccess)
        {
            return Results.Created(string.Empty, new
            {
                Key = result.Value,
                Url = storageService.GetPublicUrl(result.Value!)
            });
        }

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
