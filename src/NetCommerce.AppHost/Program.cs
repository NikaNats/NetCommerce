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

// =============================================================================
// Redis for caching, distributed locking, and basket storage
// =============================================================================
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisInsight()
    .WithLifetime(ContainerLifetime.Persistent);

// =============================================================================
// Keycloak for Identity & Access Management
// =============================================================================
var keycloak = builder.AddKeycloakContainer("keycloak")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithImport("./realms/netcommerce-realm.json");

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
    // Keycloak authentication - using realm reference for proper configuration
    .WithReference(keycloak)
    .WithReference(realm)
    .WithEnvironment("Auth__Audience", "netcommerce-api")
    .WithEnvironment("Auth__ApiScope", "netcommerce.api")
    .WithEnvironment("SWAGGERUI_CLIENTID", "netcommerce-swagger")
    .WaitFor(keycloak)
    // External endpoints for development
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();