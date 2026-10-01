#nullable enable
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.LoadTests.Assertions;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NetCommerce.LoadTests.Fixtures;

/// <summary>
///     Self-bootstrapping host for automated load suites: Testcontainers PostgreSQL + Redis,
///     the real API pipeline via TestServer, real table schemas, and a test-only
///     authentication scheme (admin/vendor/customer roles).
///
///     BOOTSTRAP NOTES (all verified empirically, including against Mvc.Testing 10.0 source):
///     - JasperFxEnvironment.AutoStartHost = true is REQUIRED (mirrors PaymentWebhookTests):
///       without it the Oakton entry-point interop dies with ObjectDisposedException inside
///       WebApplicationFactory.ConfigureHostBuilder. Set via the JasperFx package directly.
///       (Oakton itself cannot be referenced: 6.3.0 caps Microsoft.Extensions.Hosting to
///       [6.0.0, 10.0.0), unresolvable against this repo's central Hosting 10.0.12 pin.)
///     - Kestrel is deliberately NOT used: WebApplicationFactory always serves via TestServer
///       (UseKestrel() in any form never yields a listening socket here — proved by 120s of
///       refused connects plus the derived-factory delegation gap in the Mvc.Testing source).
///       NBomber therefore measures the FULL pipeline without socket transport: routing,
///       middleware, auth, Wolverine handlers, EF Core, and outbox against real PG/Redis —
///       exactly what the oversell/deadlock/leak invariants need. Raw-throughput SLOs remain
///       staging concerns for a deployed environment.
///     - Config uses WithWebHostBuilder + UseSetting (proven by PaymentWebhookTests);
///       ConfigureWebHost overrides demonstrably lose to appsettings.json fallbacks.
/// </summary>
public sealed class LoadTestHostFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>
    ///     Creates an HttpClient routed through the full in-process pipeline (TestServer).
    ///     HttpClient is thread-safe: NBomber scenarios share one instance per test.
    /// </summary>
    public HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        return client;
    }

    /// <summary>Asserts no active sagas remain (polls the metrics endpoint via the pipeline).</summary>
    public Task AssertNoSagaLeaksAsync() =>
        SagaLeakAssertions.AssertNoActiveSagasAsync(CreateClient());

    /// <summary>
    ///     Report folder for NBomber artifacts. Honors LOAD_TEST_REPORTS_DIR (absolute, used by
    ///     CI so upload-artifact finds them); defaults to the repo-relative artifacts layout.
    ///     NBomber resolves relative folders against the test process CWD (the test output dir
    ///     under dotnet test), NOT the repo root — hence the absolute override in CI.
    /// </summary>
    public static string ReportFolder(string name) => Path.Combine(
        Environment.GetEnvironmentVariable("LOAD_TEST_REPORTS_DIR") ?? "./artifacts/load-test-reports",
        name);

    /// <summary>One-line summary of server exceptions captured during the run (type + message).</summary>
    public static string ServerErrorSummary() =>
        string.Join(" ||| ", ServerErrors
            .GroupBy(e => e.GetType().FullName)
            .Select(g =>
            {
                var first = g.First();
                var inner = first.InnerException != null
                    ? $" <- {first.InnerException.GetType().Name}:{first.InnerException.Message.Split('\n')[0]}"
                    : "";
                return $"{g.Key}x{g.Count()}:{first.Message.Split('\n')[0]}{inner}";
            }));

    /// <summary>Escape hatch for diagnostics and seeding that bypasses HTTP.</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>
    ///     Request header selecting the virtual-user identity for the test auth scheme.
    ///     Kept for audit realism (IUserContext attribution); it does NOT shard the global
    ///     limiter, which evaluates pre-authentication (see VirtualClientIp).
    /// </summary>
    public const string LoadTestUserHeader = "X-LoadTest-User";

    /// <summary>
    ///     Distinct virtual-client IP for the X-Forwarded-For header, rotated per iteration.
    ///     REQUIRED for load suites: the global limiter runs BEFORE authentication and keys
    ///     loopback traffic into a single "unknown-proxy" 100-req/min bucket (verified: 291×
    ///     429s with p50 50s). Rotating loopback-trusted XFF shards partitions exactly like
    ///     distinct prod clients behind a proxy. The fixture trusts 127.0.0.0/8 below so the
    ///     ForwardedHeaders middleware honors it.
    /// </summary>
    public static string VirtualClientIp(int n) => $"10.99.{(n / 254) % 254}.{(n % 254) + 1}";

    public async ValueTask InitializeAsync()
    {
        // Entry-point interop: without this, Oakton + WAF bootstrap dies with
        // ObjectDisposedException (mirrors PaymentWebhookTests, which sets it directly).
        JasperFx.CommandLine.JasperFxEnvironment.AutoStartHost = true;

        // 1. Ephemeral infrastructure (images mirror the proven in-repo fixtures)
        _postgres = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("netcommerce_load")
            .WithUsername("postgres")
            .WithPassword("Password123!")
            .Build();

        _redis = new RedisBuilder("redis:8-alpine")
            .Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // 2. Boot the real API (TestServer dispatch; see class doc on transport honesty).
        // WithWebHostBuilder + UseSetting is the exact pattern proven by PaymentWebhookTests
        // (UseSetting keys demonstrably beat appsettings.json fallbacks like DefaultConnection);
        // ConfigureWebHost overrides demonstrably do NOT.
        var pg = PostgresConnectionString;
        var redis = RedisConnectionString;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            // Mirror PaymentWebhookTests (proven to boot Program) + ShippingDb.
            builder.UseSetting("ConnectionStrings:CatalogDb", pg);
            builder.UseSetting("ConnectionStrings:OrderingDb", pg);
            builder.UseSetting("ConnectionStrings:InventoryDb", pg);
            builder.UseSetting("ConnectionStrings:PaymentsDb", pg);
            builder.UseSetting("ConnectionStrings:FinanceDb", pg);
            builder.UseSetting("ConnectionStrings:ShippingDb", pg);
            builder.UseSetting("ConnectionStrings:postgres", pg);
            builder.UseSetting("ConnectionStrings:Redis", redis);
            builder.UseSetting("Stripe:SecretKey", "sk_test_mock");
            builder.UseSetting("Stripe:PublishableKey", "pk_test_mock");
            builder.UseSetting("Stripe:WebhookSecret", "whsec_test_secret");

            // Test-only auth: every request is authenticated with all roles.
            // Registered AFTER Program's services, so the default scheme wins over Keycloak JWT.
            // Plus an outermost exception-capture hook so burst 500s explain THEMSELVES
            // (ProblemDetails sanitizes bodies in Testing; this preserves type+message).
            builder.ConfigureServices(services =>
            {
                services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, ServerExceptionCaptureFilter>();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = LoadTestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = LoadTestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, LoadTestAuthHandler>(
                    LoadTestAuthHandler.SchemeName, _ => { });

                // Trust loopback as a proxy so per-iteration X-Forwarded-For IPs (see
                // VirtualClientIp) populate RemoteIpAddress for rate-limit partitioning.
                services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("127.0.0.0/8"));
                });
            });
        });

        // 3. Schema (CreateTablesAsync, NOT MigrateAsync — see note below).
        await ApplyDatabaseSchemaAsync();

        // 4. Prove the pipeline serves: dependency-free /health/live via the factory client.
        // (Deliberately NOT /health/ready: its Meilisearch probe has no container here.)
        using var probe = _factory.CreateClient();
        probe.Timeout = TimeSpan.FromSeconds(30);
        var ready = await probe.GetAsync("/health/live");
        ready.EnsureSuccessStatusCode();

        // 5. Prove the test auth scheme applied: /auth/session must see the default test user.
        // Without this, all load traffic falls into the single "unknown-proxy" rate-limit bucket
        // (100 req/min global) and every suite drowns in 429s while looking green at boot.
        var session = await probe.GetAsync("/api/v1/auth/session");
        session.EnsureSuccessStatusCode();
        var sessionJson = await session.Content.ReadAsStringAsync();
        if (!sessionJson.Contains("load-test-user", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Test auth scheme did not apply: /api/v1/auth/session does not report the test user. " +
                "Refusing to run load scenarios that would only measure 401/429 responses.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private async Task ApplyDatabaseSchemaAsync()
    {
        // NOTE: deliberately CreateTablesAsync, NOT MigrateAsync.
        // The Catalog model currently drifts from its committed migrations, so MigrateAsync
        // throws PendingModelChangesWarning-as-error (pre-existing: no Catalog file is touched
        // by this change set; prod --migrate-only hits the same wall until a migration is added).
        // Load suites need schema, not history fidelity — same approach as IntegrationTestFixture.
        var pg = PostgresConnectionString;
        await using var connection = new NpgsqlConnection(pg);
        await connection.OpenAsync();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE SCHEMA IF NOT EXISTS catalog;
                CREATE SCHEMA IF NOT EXISTS inventory;
                CREATE SCHEMA IF NOT EXISTS ordering;
                CREATE SCHEMA IF NOT EXISTS payments;
                CREATE SCHEMA IF NOT EXISTS finance;
                CREATE SCHEMA IF NOT EXISTS shipping;
                CREATE SCHEMA IF NOT EXISTS wolverine;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await CreateTablesAsync<CatalogDbContext>();
        await CreateTablesAsync<OrderingDbContext>();
        await CreateTablesAsync<InventoryDbContext>();
        await CreateTablesAsync<PaymentsDbContext>();
        await CreateTablesAsync<FinanceDbContext>();
        await CreateTablesAsync<ShippingDbContext>();
    }

    private async Task CreateTablesAsync<TContext>() where TContext : DbContext
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    /// <summary>
    ///     Test-only server-error capture: records the real exception behind every 500 so load
    ///     assertions can report causes instead of sanitized ProblemDetails. Runs outermost via
    ///     IStartupFilter (before Program's own middleware) and rethrows untouched.
    /// </summary>
    public static System.Collections.Concurrent.ConcurrentBag<Exception> ServerErrors { get; } = new();

    /// <summary>
    ///     Test-only authentication handler: succeeds for every request with admin, vendor,
    ///     and customer roles. Registered exclusively inside the fixture's isolated host —
    ///     production authentication is untouched.
    /// </summary>
    private sealed class LoadTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "LoadTest";

        public LoadTestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Optional per-request identity: lets each NBomber iteration act as a distinct
            // user so rate-limit partitions shard realistically. Defaults to a single user.
            var userId = Request.Headers[LoadTestHostFixture.LoadTestUserHeader].ToString();
            if (string.IsNullOrWhiteSpace(userId))
                userId = "load-test-user";

            var claims = new[]
            {
                new Claim("sub", userId),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim(ClaimTypes.Role, "vendor"),
                new Claim(ClaimTypes.Role, "customer")
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

/// <summary>
///     Collection definition sharing the self-bootstrapped host across suites.
/// </summary>
[CollectionDefinition(nameof(LoadTestCollection))]
public class LoadTestCollection : ICollectionFixture<LoadTestHostFixture>
{
}

internal sealed class ServerExceptionCaptureFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nxt) =>
            {
                // Registration proof: present on every response iff this filter is in the
                // pipeline (set before downstream runs; headers lock once the body starts).
                context.Response.Headers["X-LoadTest-Capture"] = "active";
                await nxt();

                // NOTE: a try/catch here observes NOTHING — the app's own ExceptionHandler
                // middleware legitimately swallows downstream throws after converting them to
                // 500 responses. Read the captured feature instead: ExceptionHandlerMiddleware
                // stashes the original exception there before GlobalExceptionHandler runs.
                if (context.Response.StatusCode >= 500)
                {
                    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                    if (feature?.Error is not null)
                        LoadTestHostFixture.ServerErrors.Add(feature.Error);
                }
            });
            next(app);
        };
}
