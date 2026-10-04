using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Catalog.Infrastructure.Services;

namespace NetCommerce.Api.Endpoints.Admin;

public class AdminSearchEndpoints : IEndpointGroup
{
    public void MapEndpoints(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/admin/search")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(1.0)
            .WithTags("Admin Search Management")
            .RequireAuthorization("AdminElevated")
            .RequireRateLimiting("AdminStrict");

        group.MapPost("rebuild", RebuildSearchIndex)
            .WithName("RebuildSearchIndex")
            .WithSummary("Reproject every product from PostgreSQL into Meilisearch")
            .Produces<RebuildSearchIndexResponse>(StatusCodes.Status200OK)
            .WithRequestTimeout(TimeSpan.FromMinutes(10));
    }

    private static async Task<IResult> RebuildSearchIndex(
        [FromQuery] int batchSize,
        [FromServices] ISearchIndexRebuilder rebuilder,
        ILogger<AdminSearchEndpoints> logger,
        CancellationToken cancellationToken)
    {
        // Synchronous by design: the caller is an operator watching the job,
        // and the response carries the counts. The 10-minute request-timeout
        // override above (vs the 90s default) covers large catalogs; past
        // ~100k products prefer chunked background execution instead.
        logger.LogInformation("Admin search index rebuild requested. BatchSize={BatchSize}", batchSize);

        var result = await rebuilder.RebuildAsync(batchSize, cancellationToken);

        return Results.Ok(new RebuildSearchIndexResponse(result.IndexedCount, result.BatchCount));
    }
}

public sealed record RebuildSearchIndexResponse(int IndexedCount, int BatchCount);
