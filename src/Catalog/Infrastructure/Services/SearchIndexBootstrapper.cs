using Meilisearch;
using Microsoft.Extensions.Logging;
using Index = Meilisearch.Index;

namespace NetCommerce.Catalog.Infrastructure.Services;

/// <summary>
///     One-time Meilisearch index provisioning, shared by the live projection and
///     the full rebuild. Settings updates are idempotent but expensive: firing
///     them per product turns a bulk import into thousands of Meilisearch tasks
///     and stalls indexing for minutes. Configure once per process; re-arm after
///     a cooldown so a failed attempt (Meilisearch down at startup) is retried,
///     not dropped. Extracted verbatim from ProductSearchProjectionHandler so
///     the live path and the recovery path can never disagree on settings.
/// </summary>
public static class SearchIndexBootstrapper
{
    private const string ProductsIndexName = "products";

    private static volatile bool _indexConfigured;
    private static DateTime _lastConfigAttemptUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim ConfigLock = new(1, 1);
    private static readonly TimeSpan ConfigRetryCooldown = TimeSpan.FromMinutes(5);

    public static string IndexName => ProductsIndexName;

    public static async Task EnsureConfiguredAsync(
        Index index,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (_indexConfigured)
            return;

        if (DateTime.UtcNow - _lastConfigAttemptUtc < ConfigRetryCooldown)
            return;

        await ConfigLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexConfigured)
                return;

            if (DateTime.UtcNow - _lastConfigAttemptUtc < ConfigRetryCooldown)
                return;

            _lastConfigAttemptUtc = DateTime.UtcNow;

            await index.UpdateSearchableAttributesAsync(
                ["Name", "Description", "Sku", "Tags"],
                cancellationToken);

            await index.UpdateFilterableAttributesAsync(
                ["Categories", "Price", "IsPublished", "StockQuantity"],
                cancellationToken);

            await index.UpdateRankingRulesAsync(
                [
                    "words",
                    "typo",
                    "proximity",
                    "attribute",
                    "sort",
                    "exactness"
                ],
                cancellationToken);

            _indexConfigured = true;

            logger.LogInformation("Meilisearch index '{IndexName}' configured successfully", ProductsIndexName);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Meilisearch index configuration skipped (likely already configured)");
        }
        finally
        {
            ConfigLock.Release();
        }
    }
}
