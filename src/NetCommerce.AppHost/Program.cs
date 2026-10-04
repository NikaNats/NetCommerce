using Aspire.Hosting.ApplicationModel;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// =============================================================================
// Parameters
// =============================================================================
var postgresPassword = builder.AddParameter("PostgresPassword", true);

// =============================================================================
// PostgreSQL with per-module databases
// =============================================================================
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()
    .WithPgAdmin(pgAdmin => { pgAdmin.WithHostPort(5050); })
    .WithLifetime(ContainerLifetime.Persistent);

// Module databases - each bounded context gets its own database.
// All six contexts the API migrates (see PipelineExtensions --migrate-only)
// need a resource here: without one the module silently falls back to
// DefaultConnection (localhost) via NpgsqlPoolingExtensions.
var catalogDb = postgres.AddDatabase("CatalogDb", "catalog");
var orderingDb = postgres.AddDatabase("OrderingDb", "ordering");
var inventoryDb = postgres.AddDatabase("InventoryDb", "inventory");
var paymentsDb = postgres.AddDatabase("PaymentsDb", "payments");
var financeDb = postgres.AddDatabase("FinanceDb", "finance");
var shippingDb = postgres.AddDatabase("ShippingDb", "shipping");
var keycloakDb = postgres.AddDatabase("KeycloakDb", "keycloak");

// =============================================================================
// Redis for caching, distributed locking, and basket storage
// =============================================================================
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisInsight()
    .WithLifetime(ContainerLifetime.Persistent);

// =============================================================================
// Keycloak 26 Identity Infrastructure (Zero-Trust Identity Mesh)
// =============================================================================
var keycloak = builder.AddKeycloakContainer("keycloak")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithImport("./realms/netcommerce-realm.json")
    // Keycloak 26 Standard: Use KC_BOOTSTRAP_ADMIN instead of deprecated KEYCLOAK_ADMIN
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    // Enable critical features: Token Exchange (RFC 8693) + Fine-Grained Authorization
    .WithEnvironment("KC_FEATURES", "token-exchange,admin-fine-grained-authz")
    // PostgreSQL for persistent identity storage. NOTE: Keycloak is a JVM app
    // and KC_DB_URL must be a JDBC URL — passing the Aspire database reference
    // (a .NET-style "Host=...;Port=...;..." connection string) makes the JDBC
    // driver fail with "Driver does not support the provided URL" and the
    // container exits on boot. The split KC_DB_URL_* vars avoid URL building
    // entirely. KC_DB_USERNAME/PASSWORD are also required: without them auth
    // fails even with a correct URL. TopologyTests cannot catch this (model
    // build only); it was proven by a live boot.
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL_HOST", postgres.GetEndpoint("tcp").Property(EndpointProperty.Host))
    .WithEnvironment("KC_DB_URL_PORT", postgres.GetEndpoint("tcp").Property(EndpointProperty.Port))
    .WithEnvironment("KC_DB_URL_DATABASE", "keycloak")
    .WithEnvironment("KC_DB_USERNAME", "postgres")
    .WithEnvironment("KC_DB_PASSWORD", postgresPassword)
    .WithReference(keycloakDb)
    .WaitFor(postgres)
    // Enable health and metrics endpoints for observability
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithEnvironment("KC_METRICS_ENABLED", "true");

var realm = keycloak.AddRealm("netcommerce");

// =============================================================================
// Azure Blob Storage (uses Azurite locally, Azure Blob Storage in production)
// =============================================================================
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithDataVolume();
        emulator.WithBlobPort(10000);
        emulator.WithQueuePort(10001);
        emulator.WithTablePort(10002);
    });

var blobStorage = storage.AddBlobs("blobs");

// =============================================================================
// Seq for structured logging (development)
// =============================================================================
var seq = builder.AddSeq("seq")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// =============================================================================
// Meilisearch for product search (read model)
// Provides <50ms search latency with typo tolerance, faceting, and highlighting
// =============================================================================
var meilisearchMasterKey = builder.AddParameter("meilisearch-masterkey", secret: true);
var meilisearch = builder.AddMeilisearch("meilisearch", masterKey: meilisearchMasterKey)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

// =============================================================================
// NetCommerce API
// =============================================================================
var api = builder.AddProject<NetCommerce_Api>("netcommerce-api")
    // Database references
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithReference(orderingDb).WaitFor(orderingDb)
    .WithReference(inventoryDb).WaitFor(inventoryDb)
    .WithReference(paymentsDb).WaitFor(paymentsDb)
    .WithReference(financeDb).WaitFor(financeDb)
    .WithReference(shippingDb).WaitFor(shippingDb)
    // Redis
    .WithReference(redis).WaitFor(redis)
    // Blob storage
    .WithReference(blobStorage).WaitFor(storage)
    // Seq logging
    .WithReference(seq).WaitFor(seq)
    // Meilisearch for product search
    .WithReference(meilisearch).WaitFor(meilisearch)
    // Keycloak authentication - using realm reference for proper configuration
    .WithReference(keycloak)
    .WithReference(realm)
    // Zero-Trust Identity Configuration
    .WithEnvironment("Auth__Audience", "netcommerce-api")
    .WithEnvironment("Auth__ApiScope", "netcommerce.api")
    // Service-to-Service Identity (Client Credentials)
    .WithEnvironment("Auth__ClientId", "netcommerce-api")
    .WithEnvironment("Auth__ClientSecret", "netcommerce-api-secret") // In prod, use Secret Store
    // Token Introspection for instant revocation (Kill Switch)
    .WithEnvironment("Auth__IntrospectionEnabled", "true")
    .WithEnvironment("SWAGGERUI_CLIENTID", "netcommerce-swagger")
    .WaitFor(keycloak)
    // External endpoints for development
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health/ready");

// =============================================================================
// NetCommerce Web (Next.js 16 storefront / BFF layer)
// =============================================================================
// Aspire 13.5.4 ships NO JavaScript/Node application resource: AddNpmApp,
// AddViteApp and AddNodeApp do not exist in Aspire.Hosting.dll (verified by
// reflecting over the pinned 13.5.4 assembly, 108 distinct Add* methods, no
// Npm/Vite/Node/JavaScript member), and no Aspire Node package is available in
// the NuGet cache. `builder.AddNpmApp(...)` therefore does not compile.
//
// AddExecutable is the real primitive for launching a non-.NET process that
// reports an HTTP endpoint. It launches the executable DIRECTLY, with no shell,
// and with UseShellExecute=false — so PATHEXT is NOT consulted and `.cmd`
// shims are invisible under a bare name.
//
// Windows note: bare `npm` is a Git-Bash shell script, not a Win32 executable,
// so Process.Start throws Win32Exception "The system cannot find the file
// specified". Verified by execution against Process.Start with a passing
// control. `npm.cmd` is the real command shim. Use bare `npm` elsewhere.
var npmExecutable = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

// Port: the dev script is `next dev --port 3000` (a literal — npm runs scripts
// through cmd.exe on Windows, which does NOT expand ${PORT:-3000}; verified:
// Next rejects the unexpanded string with "is not a non-negative number").
// Aspire does not inject PORT, so the process port is fixed at 3000 and the
// endpoint must advertise that same port.
var web = builder.AddExecutable("netcommerce-web", npmExecutable, "run", "dev")
    .WithWorkingDirectory("../Web/netcommerce-web")
    // Server-side calls to the API go through Aspire service discovery.
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("http"))
    // CSP connect-src allowlist (next.config.ts readApiOrigin): the browser opens
    // the SignalR hub (/api/messages) directly on the API origin — a Next rewrite
    // cannot proxy the WebSocket Upgrade handshake. Same value as API_BASE_URL,
    // exposed under a separate name so the CSP builder's intent is explicit.
    .WithEnvironment("PUBLIC_API_ORIGIN", api.GetEndpoint("http"))
    // Client-side hub origin (use-order-saga.ts hubOrigin): NEXT_PUBLIC_ is the
    // only env Next inlines into the browser bundle. Must match PUBLIC_API_ORIGIN;
    // when unset the hook fails loudly with a same-origin 404 rather than
    // silently reaching somewhere unexpected.
    .WithEnvironment("NEXT_PUBLIC_API_ORIGIN", api.GetEndpoint("http"))
    // CSP img-src allowlist (next.config.ts readStorageOrigin): must match the
    // Storage__CdnBaseUrl origin in src/Api/appsettings*.json (MinIO,
    // http://localhost:9000/netcommerce in development — cleartext http, which
    // 'self' data: https: alone hard-blocks). A blanket `http:` is deliberately
    // NOT used: it would permit any cleartext origin in production too.
    .WithEnvironment("STORAGE_ORIGIN", "http://localhost:9000")
    // Keycloak's browser-facing authorize URL, used to build the PKCE redirect.
    .WithEnvironment("KEYCLOAK_BASE_URL", keycloak.GetEndpoint("http"))
    .WithEnvironment("KEYCLOAK_REALM", "netcommerce")
    .WithEnvironment("KEYCLOAK_CLIENT_ID", "netcommerce-web")
    // Pins the origin used to build the OAuth redirect_uri. Both /login and
    // /callback must derive the SAME value or Keycloak rejects the exchange;
    // reading it from the Host header is ambiguous behind any proxy. In dev the
    // value is the fixed port the dev script binds to.
    .WithEnvironment("PUBLIC_ORIGIN", "http://localhost:3000")
    // Session store. Aspire's supported idiom for sharing a connection string is
        // .WithReference(resource), which injects ConnectionStrings__<name>. The
        // frontend reads a plain REDIS_URL, so it is configured from the same
        // resource — via the IResourceWithConnectionString interface, which is the
        // only public accessor (BuildConnectionString on RedisResource is private;
        // verified by reflecting over Aspire.Hosting.Redis 13.5.3).
        //
        // Without it the BFF falls back to a process-local Map: correct for one
        // replica, silently broken for two, because a session created on one replica
        // is invisible to the next. The frontend refuses to boot in production on a
        // process-local store, so this reference is what keeps the running system on
        // the shared path.
        .WithReference(redis)
                // ReferenceExpression is the supported way to forward a resource's
                // connection string under another env-var name: it defers evaluation to
                // runtime, so no connection is opened while the AppHost is still
                // composing its model. BuildConnectionString/GetConnectionStringAsync are
                // not usable here — the former is private on RedisResource (verified by
                // reflection over Aspire.Hosting.Redis 13.5.3), the latter would resolve
                // the endpoint during composition.
                .WithEnvironment(
                    "REDIS_URL",
                    redis.Resource.ConnectionStringExpression)
    // NOTE: isProxied: false is LOAD-BEARING, not a preference.
    // EndpointAnnotation.IsProxied defaults to TRUE, so omitting it (or adding
    // .WithExternalHttpEndpoints()) makes DCP proxy this endpoint — and for a
    // non-container resource DCP throws InvalidOperationException at startup
    // when Port and TargetPort are equal ("Non-container resources cannot be
    // proxied when both TargetPort and Port are specified with the same
    // value"). The port is fixed at 3000 on both sides because PUBLIC_ORIGIN
    // and the dev script (`next dev --port 3000`) are pinned to it, so the
    // endpoint must be handled and exposed by the resource itself: the browser
    // reaches Next at http://localhost:3000 with no proxy in between.
    // TopologyTests.Web_Should_Use_Direct_Fixed_Port_Endpoint pins this.
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http", isProxied: false)
    .WithReference(api).WaitFor(api)
    .WithReference(keycloak).WaitFor(keycloak)
    // The session store connects lazily on first use, so a Next that starts
    // before Redis is listening would fail the first real login rather than at
    // startup — the worst place to discover it.
    .WaitFor(redis);

builder.Build().Run();
