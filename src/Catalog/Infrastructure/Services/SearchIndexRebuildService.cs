using Meilisearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Models;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Domain.Shared;

namespace NetCommerce.Catalog.Infrastructure.Services;

/// <summary>
///     Full search-index rebuild: reprojects every product from PostgreSQL into
///     Meilisearch. Disaster-recovery path for a lost Meili volume (which the
///     per-event projection cannot heal — it only fires on product changes).
/// </summary>
public interface ISearchIndexRebuilder
{
    Task<RebuildSearchIndexResult> RebuildAsync(int batchSize, CancellationToken cancellationToken);
}

public sealed record RebuildSearchIndexResult(int IndexedCount, int BatchCount);

/// <summary>
///     Reads the write model DIRECTLY (CatalogDbContext, AsNoTracking), bypassing
///     the HybridCache product decorator on purpose: a rebuild that runs through
///     the cache could reindex stale snapshots for up to the L2 TTL, defeating
///     the recovery. Stock resolves in one round trip per page via the batch
///     query. Offset paging is proportionate here (admin-triggered, catalogs in
///     the hundreds-to-thousands); past ~100k products switch to keyset paging
///     plus cascading Wolverine batch messages so a restart resumes mid-stream.
/// </summary>
public sealed class SearchIndexRebuildService : ISearchIndexRebuilder
{
    private readonly CatalogDbContext _db;
    private readonly IStockQueryService _stock;
    private readonly MeilisearchClient _meilisearch;
    private readonly ILogger<SearchIndexRebuildService> _logger;

    public SearchIndexRebuildService(
        CatalogDbContext db,
        IStockQueryService stock,
        MeilisearchClient meilisearch,
        ILogger<SearchIndexRebuildService> logger)
    {
        _db = db;
        _stock = stock;
        _meilisearch = meilisearch;
        _logger = logger;
    }

    public async Task<RebuildSearchIndexResult> RebuildAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        batchSize = batchSize is > 0 and <= 1000 ? batchSize : 100;

        var index = _meilisearch.Index(SearchIndexBootstrapper.IndexName);
        await SearchIndexBootstrapper.EnsureConfiguredAsync(index, _logger, cancellationToken);

        var indexed = 0;
        var batches = 0;

        while (true)
        {
            var page = await _db.Products
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Skip(indexed)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
                break;

            var quantities = await _stock.GetAvailableQuantitiesAsync(
                page.Select(p => p.Id),
                cancellationToken);

            var documents = page
                .Select(p => ProductSearchDocument.FromProduct(
                    p,
                    quantities.TryGetValue(p.Id, out var quantity) ? quantity : 0))
                .ToList();

            await index.AddDocumentsAsync(documents, "Id", cancellationToken);

            indexed += page.Count;
            batches++;
            _logger.LogInformation(
                "Search index rebuild progress: {Indexed} products in {Batches} batches",
                indexed,
                batches);

            if (page.Count < batchSize)
                break;
        }

        _logger.LogInformation(
            "Search index rebuild complete: {Indexed} products in {Batches} batches",
            indexed,
            batches);
        return new RebuildSearchIndexResult(indexed, batches);
    }
}
