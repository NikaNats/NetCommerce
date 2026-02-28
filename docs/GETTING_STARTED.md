# Getting Started

This guide walks through setting up a local NetCommerce development environment from scratch.

## Prerequisites

| Requirement | Minimum Version | Purpose |
|---|---|---|
| .NET SDK | 10.0.100 | Runtime and build toolchain |
| Docker Desktop | 4.30+ | Aspire-managed infrastructure containers |
| Git | 2.40+ | Source control |

Verify installations:

```powershell
dotnet --version    # 10.0.100
docker --version    # Docker version 27.x
git --version       # git version 2.x
```

Docker Desktop must be running before launching the application. Aspire manages all infrastructure containers automatically — no manual `docker-compose` setup is needed.

## Clone and Restore

```powershell
git clone https://github.com/NikaNats/NetCommerce.git
cd NetCommerce
dotnet restore NetCommerce.slnx
```

The solution uses **Central Package Management** (`Directory.Packages.props`) with lock files (`packages.lock.json`). The first restore downloads all dependencies and generates lock files.

## Launch with Aspire

```powershell
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

Aspire provisions the following infrastructure automatically:

| Resource | Container | Access |
|---|---|---|
| PostgreSQL 17 | `postgres` | Port auto-assigned, PgAdmin at `:5050` |
| Redis 8 | `redis` | RedisInsight available via Aspire dashboard |
| Keycloak | `keycloak` | Admin console via Aspire dashboard link |
| Seq | `seq` | Log viewer via Aspire dashboard link |
| MeiliSearch | `meilisearch` | Search UI via Aspire dashboard link |
| Azurite | `storage` | Blob `:10000`, Queue `:10001`, Table `:10002` |

### Database Schemas

In development mode, EF Core migrations run automatically on startup for all six schemas:

| Schema | DbContext | Bounded Context |
|---|---|---|
| `catalog` | `CatalogDbContext` | Product catalog |
| `ordering` | `OrderingDbContext` | Orders and saga state |
| `inventory` | `InventoryDbContext` | Stock and reservations |
| `payments` | `PaymentsDbContext` | Payment transactions |
| `shipping` | `ShippingDbContext` | Shipment tracking |
| `finance` | `FinanceDbContext` | Audit and reconciliation |

### Aspire Dashboard

After launching, the Aspire dashboard opens automatically at `https://localhost:15888` (port may vary). The dashboard provides:

- **Resources** — container status, connection strings, endpoints
- **Traces** — distributed traces across all modules
- **Logs** — aggregated structured logs from Seq
- **Metrics** — OpenTelemetry metrics

## Verify the Setup

### Health Check

```powershell
curl https://localhost:5001/health/ready
```

Returns `200 OK` with `Healthy` when all infrastructure dependencies are available.

### API Documentation

Navigate to the Scalar API docs at:

```
https://localhost:5001/scalar
```

The OpenAPI specification is available at `/openapi/v1.json`.

### Create a Test Product

```powershell
# Obtain a vendor token from Keycloak (see Auth section)
$token = "Bearer <your-token>"

curl -X POST https://localhost:5001/api/v1/products `
  -H "Authorization: $token" `
  -H "Content-Type: application/json" `
  -d '{
    "title": "Test Product",
    "description": "A test product",
    "sku": "TEST-001",
    "price": { "amount": 29.99, "currency": "GEL" },
    "categoryId": "<category-guid>"
  }'
```

## Authentication Setup

NetCommerce uses **Keycloak** as the identity provider. Aspire pre-configures a `netcommerce` realm with the following features:

- Token exchange
- Admin fine-grained authorization
- PKCE-based authorization code flow

### Default Configuration

| Setting | Value |
|---|---|
| Realm | `netcommerce` |
| Client ID | `netcommerce-api` |
| API Scope | `netcommerce.api` |
| Audience | `netcommerce-api` |
| Token Introspection | Enabled |

### Authorization Policies

| Policy | Access Level |
|---|---|
| `AdminOnly` | Platform administrators |
| `VendorOnly` | Product vendors/sellers |
| `CustomerOnly` | End customers |
| `OwnerOnly` | Resource owner (own data) |
| `AdminElevated` | Elevated admin operations (finance, DLQ, recovery) |

The BFF auth endpoints at `/api/v1/auth` handle token exchange, refresh, revocation, and session introspection. Direct browser–Keycloak communication is never required for API clients.

## Configuration

Application settings are in `src/Api/appsettings.json` and environment-specific overrides. Key configuration sections:

| Section | Purpose |
|---|---|
| `ConnectionStrings` | Database and Redis connection strings (injected by Aspire) |
| `Jwt` | JWT validation parameters (Authority, Audience) |
| `Auth` | Keycloak endpoint URLs, client credentials |
| `Stripe` | Stripe API keys and webhook secret |
| `Storage` | Azure Blob connection string |
| `ReservationCleanup` | Cleanup interval and expiry thresholds |
| `GracePeriod` | Order grace period duration |
| `Sentry` | Error monitoring DSN |
| `Serilog` | Structured logging configuration |
| `Finance:Alerting` | Reconciliation alert thresholds |

All secrets are injected via environment variables in production. Aspire handles injection of connection strings and service endpoints in development.

## Build Commands

```powershell
# Build entire solution
dotnet build NetCommerce.slnx

# Run all tests
dotnet test NetCommerce.slnx -v minimal --nologo

# Run specific test project
dotnet test tests/NetCommerce.Domain.Tests --nologo

# Publish Native AOT build
dotnet publish src/Api/NetCommerce.Api.csproj -c Release -r linux-x64 -p:PublishAot=true

# Generate Wolverine handler code (for AOT verification)
dotnet run --project src/Api/NetCommerce.Api.csproj -- codegen write
```

## IDE Setup

### Visual Studio / Rider

Open `NetCommerce.slnx` directly. The solution uses the modern `.slnx` format with artifacts output redirection — all build outputs go to the top-level `artifacts/` directory.

### VS Code

Install the C# Dev Kit extension. The solution is pre-configured with:

- Build tasks in `.vscode/tasks.json`
- Launch configurations for Aspire debugging

## Troubleshooting First Run

| Symptom | Cause | Fix |
|---|---|---|
| Docker containers fail to start | Docker Desktop not running | Start Docker Desktop, wait for engine ready |
| Port conflicts on 5050 | PgAdmin port in use | Stop other services using port 5050 |
| Migration errors | Schema drift | Delete the PostgreSQL volume: `docker volume rm netcommerce_postgres-data` |
| Keycloak realm not found | First startup delay | Wait for Keycloak container to complete realm import |
| MeiliSearch index empty | No products created yet | Create products via the API, search index syncs automatically |

For additional troubleshooting, see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Next Steps

- [Architecture](ARCHITECTURE.md) — understand the modular monolith design
- [API Reference](API_REFERENCE.md) — explore all REST endpoints
- [Testing](TESTING.md) — run and write tests
- [Contributing](CONTRIBUTING.md) — development workflow and conventions
