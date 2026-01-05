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

// Module databases - each bounded context gets its own database
var catalogDb = postgres.AddDatabase("CatalogDb", "catalog");
var orderingDb = postgres.AddDatabase("OrderingDb", "ordering");
var inventoryDb = postgres.AddDatabase("InventoryDb", "inventory");
var paymentsDb = postgres.AddDatabase("PaymentsDb", "payments");
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
    // Use PostgreSQL instead of H2 for persistent identity storage
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL", keycloakDb)
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

builder.Build().Run();
