# Deployment

Deployment guide for NetCommerce covering local development, Docker, Native AOT, and production environments.

## Deployment Modes

| Mode | Startup | Memory | Container Size | Use Case |
|---|---|---|---|---|
| **JIT (Development)** | ~2.5s | ~180 MB | ~230 MB | Local dev, debugging |
| **Native AOT (Production)** | ~80ms | ~100 MB | ~65 MB | Production, Kubernetes |

## Local Development

### Aspire-Managed Infrastructure

```powershell
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

Aspire provisions all infrastructure containers automatically. No manual Docker Compose setup is needed.

### Environment Variables (Aspire-Injected)

Aspire injects these automatically in development:

| Variable | Source |
|---|---|
| `ConnectionStrings__CatalogDb` | PostgreSQL container |
| `ConnectionStrings__OrderingDb` | PostgreSQL container |
| `ConnectionStrings__InventoryDb` | PostgreSQL container |
| `ConnectionStrings__PaymentsDb` | PostgreSQL container |
| `ConnectionStrings__redis` | Redis container |
| `ConnectionStrings__blobs` | Azurite container |
| `ConnectionStrings__seq` | Seq container |
| `ConnectionStrings__meilisearch` | MeiliSearch container |
| `Auth__Audience` | `netcommerce-api` |
| `Auth__ApiScope` | `netcommerce.api` |
| `Auth__ClientId` | `netcommerce-api` |

## Docker Build

### Standard JIT Build

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Api/NetCommerce.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "NetCommerce.Api.dll"]
```

### Native AOT Build

```powershell
docker build -t netcommerce-api-aot -f src/Api/Dockerfile .
```

**Build Timeline:**
- First build: ~5–7 minutes (ILC compilation takes 3–5 minutes)
- Subsequent builds: ~30 seconds (layer caching)

**AOT Build Requirements:**
1. Source-generated JSON via `ApiJsonContext` (~80+ types registered)
2. Wolverine `TypeLoadMode.Static` — pre-generated handler code
3. No runtime reflection in critical paths
4. All types in `ApiJsonContext` for serialization

### Native AOT Container Properties

| Property | Value |
|---|---|
| Base Image | Ubuntu Chiseled (no shell) |
| User | Non-root (UID 1654) |
| Binary Size | ~45 MB |
| Container Size | ~65 MB |
| Startup Time | ~80ms |
| Memory (Idle) | ~100 MB |

### Performance Comparison

| Metric | Native AOT (Chiseled) | JIT (Warmed) |
|---|---|---|
| Startup | ~80ms | ~2.5s |
| Memory (Idle) | ~100 MB | ~180 MB |
| Requests/sec | ~8,500 | ~7,200 |
| P95 Latency | ~12ms | ~18ms |
| Binary Size | ~45 MB | ~85 MB |
| Container Size | ~65 MB | ~230 MB |

## Production Configuration

### Required Environment Variables

| Variable | Description | Example |
|---|---|---|
| `ConnectionStrings__CatalogDb` | Catalog database | `Host=pg;Database=netcommerce;...` |
| `ConnectionStrings__OrderingDb` | Ordering database | Same host, separate schema |
| `ConnectionStrings__InventoryDb` | Inventory database | Same host, separate schema |
| `ConnectionStrings__PaymentsDb` | Payments database | Same host, separate schema |
| `ConnectionStrings__redis` | Redis connection | `redis:6379` |
| `ConnectionStrings__blobs` | Azure Blob Storage | Azure connection string |
| `ConnectionStrings__seq` | Seq ingestion URL | `http://seq:5341` |
| `ConnectionStrings__meilisearch` | MeiliSearch URL | `http://meilisearch:7700` |
| `Jwt__Authority` | Keycloak realm URL | `https://keycloak/realms/netcommerce` |
| `Jwt__Audience` | JWT audience | `netcommerce-api` |
| `Auth__TokenEndpoint` | Token endpoint | `https://keycloak/.../token` |
| `Auth__IntrospectionEndpoint` | Introspection URL | `https://keycloak/.../introspect` |
| `Auth__ClientId` | API client ID | `netcommerce-api` |
| `Auth__ClientSecret` | API client secret | (secret) |
| `Stripe__SecretKey` | Stripe API key | `sk_live_...` |
| `Stripe__WebhookSecret` | Webhook signing secret | `whsec_...` |

### Optional Configuration

| Variable | Default | Description |
|---|---|---|
| `ReservationCleanup__IntervalMinutes` | `5` | Cleanup job interval |
| `ReservationCleanup__ExpiryMinutes` | `30` | Reservation expiry |
| `GracePeriod__DurationMinutes` | `5` | Order grace period |
| `Finance__Alerting__DiscrepancyAlertThreshold` | `100` | Dollar threshold for alerts |
| `Finance__Alerting__SendEmailAlerts` | `false` | Email alert toggle |
| `Finance__Alerting__FinanceAlertEmail` | — | Alert recipient |
| `Finance__Alerting__PagerDutyRoutingKey` | — | PagerDuty integration |
| `Sentry__Dsn` | — | Sentry error tracking |

## Database Migrations

### Development (Automatic)

In development, EF Core migrations run automatically on startup for all six schemas:

```csharp
// Program.cs (development only)
await app.MigrateDatabaseAsync<CatalogDbContext>();
await app.MigrateDatabaseAsync<OrderingDbContext>();
await app.MigrateDatabaseAsync<InventoryDbContext>();
await app.MigrateDatabaseAsync<PaymentsDbContext>();
await app.MigrateDatabaseAsync<FinanceDbContext>();
await app.MigrateDatabaseAsync<ShippingDbContext>();
```

### Production (Manual)

Run migrations as a separate step before deployment:

```powershell
# Generate migration script
dotnet ef migrations script --project src/Catalog/Catalog.Infrastructure --context CatalogDbContext --idempotent -o migrations/catalog.sql

# Apply to production
psql -h prod-postgres -U admin -d netcommerce -f migrations/catalog.sql
```

**Wolverine Tables:**

Wolverine creates its own tables (`wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`, saga state tables) in the `ordering` schema. These are managed automatically by Wolverine — do not modify manually.

## Health Checks

| Endpoint | Purpose | Checks |
|---|---|---|
| `GET /health/ready` | Readiness probe | PostgreSQL, Redis, MeiliSearch connectivity |
| `GET /health/live` | Liveness probe | Process alive |

### Kubernetes Probes

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 5
```

## Wolverine Codegen

For Native AOT, Wolverine handler code must be pre-generated:

```powershell
dotnet run --project src/Api/NetCommerce.Api.csproj -- codegen write
```

This writes generated handler code to `Internal/Generated/WolverineHandlers/`. These files must be committed to source control for AOT builds.

## AOT Verification

Run the 5-checkpoint AOT verification script:

```powershell
.\scripts\Verify-NativeAOT.ps1 -CheckpointsToRun "1,2,3,4,5"
```

See [NATIVE_AOT_VERIFICATION.md](NATIVE_AOT_VERIFICATION.md) for checkpoint details.

## Build Configuration

### Directory.Build.props

Key build settings applied to all projects:

| Setting | Value | Purpose |
|---|---|---|
| `TargetFramework` | `net10.0` | .NET 10 |
| `TreatWarningsAsErrors` | `true` (Release/CI) | Zero-warning policy |
| `IsAotCompatible` | `true` (non-test) | AOT compatibility |
| `EnableTrimAnalyzer` | `true` | Trim analysis |
| `EnableAotAnalyzer` | `true` (AOT projects) | AOT analysis |
| `Deterministic` | `true` | Reproducible builds |
| `RestorePackagesWithLockFile` | `true` | Lock file pinning |
| `NuGetAudit` | `true` | Vulnerability scanning |
| `NuGetAuditLevel` | `moderate` | Audit threshold |
| `ControlFlowGuard` | `Guard` | Binary hardening |

### CI Build

```powershell
# CI pipeline
dotnet restore NetCommerce.slnx --locked-mode
dotnet build NetCommerce.slnx -c Release --no-restore
dotnet test NetCommerce.slnx -c Release -v minimal --nologo --no-build
```

The `--locked-mode` flag ensures the lock file is respected and no package resolution occurs during CI.

## Rollback Procedure

1. Deploy the previous container image version
2. Verify health checks pass (`/health/ready`)
3. Check Wolverine outbox for pending messages — they will continue processing after rollback
4. Monitor DLQ for any messages that fail due to schema incompatibility

### Wolverine Serialization Compatibility

Wolverine saga state and outbox messages are serialized with fully qualified type names. Schema changes that rename types or move namespaces can break in-flight messages.

**For breaking changes:**
1. Clear Wolverine tables before deployment (development)
2. Use type forwarding for production (see [PHASE_5_SERIALIZATION_MIGRATION.md](PHASE_5_SERIALIZATION_MIGRATION.md))

## Related Documentation

- [Getting Started](GETTING_STARTED.md) — local development setup
- [Operations](OPERATIONS.md) — monitoring and maintenance
- [Native AOT](NATIVE_AOT_VERIFICATION.md) — AOT verification process
- [Architecture](ARCHITECTURE.md) — system design
