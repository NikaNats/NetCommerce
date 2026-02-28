#nullable enable
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Meilisearch;

namespace NetCommerce.Integration.Tests.Fixtures;

/// <summary>
///     Optional MeiliSearch Testcontainer support for integration tests
///     that exercise the product search projection pipeline.
///
///     <para>
///     <b>Usage — Option A:</b> Drop-in addition to <see cref="IntegrationTestFixture"/>.
///     Add the fields below and call the helper methods at the right lifecycle points.
///     </para>
///
///     <code>
///     // In IntegrationTestFixture:
///     private MeiliSearchTestHelper? _meilisearchHelper;
///     public MeilisearchClient? MeiliSearchClient => _meilisearchHelper?.Client;
///
///     // In InitializeAsync(), after containers start:
///     _meilisearchHelper = new MeiliSearchTestHelper();
///     await _meilisearchHelper.StartAsync();
///
///     // In ConfigureServices (BuildTestHostAsync):
///     if (_meilisearchHelper != null)
///         services.AddSingleton(_meilisearchHelper.Client);
///
///     // In DisposeAsync():
///     if (_meilisearchHelper != null)
///         await _meilisearchHelper.DisposeAsync();
///     </code>
///
///     <para>
///     <b>Usage — Option B:</b> Call <see cref="ResetSearchIndexAsync"/> in
///     <see cref="IntegrationTestBase.InitializeAsync"/> alongside the existing
///     <c>ResetDatabaseAsync()</c> call.
///     </para>
///
///     <code>
///     // In IntegrationTestBase.InitializeAsync():
///     await Fixture.ResetDatabaseAsync();
///     await Fixture.ResetMeiliSearchAsync();   // ← add this line
///     TestPaymentGateway.Reset();
///     </code>
/// </summary>
public sealed class MeiliSearchTestHelper : IAsyncDisposable
{
    private const string MasterKey = "test-master-key";
    private const string ProductsIndex = "products";
    private const ushort DefaultPort = 7700;

    private IContainer _container = null!;
    private MeilisearchClient _client = null!;

    /// <summary>
    ///     MeiliSearch client connected to the test container.
    /// </summary>
    public MeilisearchClient Client =>
        _client ?? throw new InvalidOperationException("MeiliSearch not started. Call StartAsync first.");

    /// <summary>
    ///     The base URL of the MeiliSearch test container.
    /// </summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>
    ///     Starts a MeiliSearch container using the generic Testcontainers builder.
    /// </summary>
    public async Task StartAsync()
    {
        _container = new ContainerBuilder("getmeili/meilisearch:v1.12")
            .WithPortBinding(DefaultPort, true)
            .WithEnvironment("MEILI_MASTER_KEY", MasterKey)
            .WithEnvironment("MEILI_NO_ANALYTICS", "true")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(DefaultPort)
                    .ForPath("/health")))
            .Build();

        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(DefaultPort);
        Url = $"http://{host}:{port}";

        _client = new MeilisearchClient(Url, MasterKey);

        // Ensure the products index exists (mirrors production setup)
        await _client.CreateIndexAsync(ProductsIndex, "Id");
    }

    /// <summary>
    ///     Deletes all documents from the "products" index without dropping the index.
    ///     Intended to be called between tests for clean isolation.
    /// </summary>
    public async Task ResetSearchIndexAsync()
    {
        if (_client is null) return;

        try
        {
            var index = _client.Index(ProductsIndex);
            var task = await index.DeleteAllDocumentsAsync();
            // Wait for the deletion to complete (MeiliSearch is async)
            await _client.WaitForTaskAsync(task.TaskUid, timeoutMs: 10_000);
        }
        catch (MeilisearchApiError ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // Index doesn't exist yet — nothing to reset
            await _client.CreateIndexAsync(ProductsIndex, "Id");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
