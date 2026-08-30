# NetCommerce 2026 Production Readiness & Deployment Master Guide
This comprehensive implementation guide translates the NetCommerce architectural vision into a strict, executable DevOps and Cloud-Native workflow. Built on **.NET 10**, **Native AOT**, and a **Modular Monolith** topology, this runbook ensures financial-grade integrity, zero-trust security, and extreme resilience under high-contention scenarios (e.g., flash sales).

---

## 🏗️ Architectural Context & 2026 Paradigms
Before deployment, understand the core paradigms driving this infrastructure:
1. **Modular Monolith over Microservices**: Bounded contexts (Catalog, Ordering, Inventory, etc.) share a process but isolate data via **PostgreSQL Schemas** and communicate via **Wolverine Transactional Outbox**. This eliminates network latency and distributed transaction taxes.
2. **Native AOT & Chiseled Containers**: The API compiles to native machine code, eliminating the JIT compiler. Deployed on Ubuntu Chiseled (no shell, no package manager), reducing the attack surface and startup time to ~80ms.
3. **Partitioned Sequential Messaging**: High-contention inventory operations are routed through Wolverine partitions, converting database row-locks into in-memory queue scheduling, eliminating deadlocks during flash sales.
4. **Triple-Lock Financial Integrity**: Daily T+1 reconciliation compares Internal Ledger vs. PSP (Stripe) vs. Immutable Audit Logs to detect "Ghost Charges".

---

## Phase 1: Local Verification & Pre-Production Gates
*Goal: Prove the build is stable, AOT-compliant, and architecturally sound before CI/CD.*

### 1.1 The 608-Test Suite & Architecture Guards
NetCommerce enforces Clean Architecture via `NetArchTest`. Domain layers cannot reference Infrastructure.
```bash
# Run all unit, integration, and architecture tests
dotnet test NetCommerce.slnx -v minimal --nologo

# Explicitly verify Clean Architecture boundaries
dotnet test tests/NetCommerce.Architecture.Tests --nologo
```

### 1.2 Native AOT 5-Checkpoint Verification
Runtime reflection is forbidden in production. The `Verify-NativeAOT.ps1` script validates AOT readiness:
```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/Verify-NativeAOT.ps1
```
* **Checkpoint 1 (Silent Killer)**: Scans for `IL2026`/`IL3050` warnings. Critical paths (endpoints, handlers) must have zero warnings.
* **Checkpoint 2 (Ghost Code)**: Verifies Wolverine `TypeLoadMode.Static` successfully pre-generates handler code (WARN with dummy DB is expected via `TypeLoadMode.Auto` fallback).
* **Checkpoint 3 (Binary Anatomy)**: Validates the Docker image is `<100MB` ideal / `~468MB` with `noble-chiseled-extra` (ICU for GEL) and lacks `/bin/sh` (Chiseled).
* **Checkpoint 4 (Smoke Test)**: Boots the AOT binary in `<100ms` (Linux; Windows Docker Desktop adds virt overhead).
* **Checkpoint 5 (Thread-Pull)**: Hits `/health/ready` and `/api/v1/products` to verify EF Core and `ApiJsonContext` source generation.

### 1.3 Local Aspire Topology
Spin up the full infrastructure mesh locally via .NET Aspire 13.1:
```bash
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```
*This provisions Postgres, Redis, Keycloak, Seq, and Meilisearch with automatic connection string injection.*

---

## Phase 2: Zero-Trust Security & Secrets Management
*Goal: Eliminate hardcoded secrets. Enforce defense-in-depth.*

> 🛡️ **2026 Best Practice**: Never use `appsettings.json` for production secrets. Use **Azure Key Vault**, **AWS Secrets Manager**, or **Kubernetes External Secrets Operator**.

### 2.1 Critical Environment Variables
Inject these into your K8s Deployment or Container App environment:

```env
# --- Database & Cache (Routed via PgBouncer & Redis Cluster) ---
ConnectionStrings__CatalogDb=Host=pgbouncer;Database=netcommerce_catalog;...
ConnectionStrings__OrderingDb=Host=pgbouncer;Database=netcommerce_ordering;...
ConnectionStrings__redis=redis-cluster:6379,password=...,ssl=true

# --- Identity (Keycloak Zero-Trust Mesh) ---
Auth__AuthServerUrl=https://keycloak.prod.internal/realms/netcommerce  # or Keycloak__AuthServerUrl
Auth__Realm=netcommerce
Auth__Audience=netcommerce-api
Auth__ClientId=netcommerce-api
Auth__ClientSecret=<FROM_KEY_VAULT>
Auth__IntrospectionEnabled=true # Enables the "Kill Switch" for instant token revocation

# --- Payments (Stripe Webhook-First Pattern) ---
Stripe__SecretKey=sk_live_<FROM_KEY_VAULT>
Stripe__WebhookSecret=whsec_<FROM_KEY_VAULT>

# --- Admin Elevated Authorization (Defense-in-Depth) ---
# P0-3 Fail-Closed: Requires Admin Role + API Key (Strict) or API Key OR fresh auth_time (Flexible)
# In Strict (prod default), missing/invalid key + stale auth_time = DENY. Startup fails closed if key <32 chars in Prod/Staging.
Auth__AdminElevated__ApiKey=<STRONG_64_CHAR_RANDOM_STRING>  # ≥32 chars, e.g., openssl rand -hex 32
Auth__AdminElevated__SecurityMode=Strict                    # Strict | Flexible | DevelopmentOnly (dev only)
# Legacy alias still bound (migration): AdminApiKey__ApiKey

# --- PII Vault (AES-256-GCM Encryption at Rest) ---
PiiVault__MasterKeyUri=<AZURE_KEY_VAULT_KMS_URI> # e.g., https://myvault.vault.azure.net/keys/netcommerce-pii/
```

### 2.2 Rate Limiting & CORS
The API enforces strict rate limits via `System.Threading.RateLimiting` (now with `ForwardedHeaders` + `GetRateLimitPartitionKey` for ALB/IPv6).
* **Global**: 100 req/min per IP (partitioned by `user:sub` or normalized IP).
* **AuthStrict**: 5 req/min (prevents brute force on `/api/v1/auth/token`).
* **AdminStrict**: 10 req/min per admin user.
* **CORS**: Ensure `Cors__AllowedOrigins` is strictly limited to your production frontend domains. **Never use `*`**.

---

## Phase 3: Infrastructure Provisioning (Production Topology)
*Goal: Provision resilient, high-concurrency infrastructure.*

| Component | 2026 Production Topology | NetCommerce Integration Notes |
| :--- | :--- | :--- |
| **PostgreSQL 17** | **PgBouncer (Transaction Mode)** | The app uses `NpgsqlPoolingExtensions` strict pooling (`maxPoolSize: 30` Catalog, `25` Inventory, `20` others = 130/pod → 390/3pods → `max_connections` 400 or PgBouncer). **Must** support 6 isolated schemas. |
| **Redis 8** | **Redis Cluster (HA)** | Used for Basket and HybridCache L2. **Kill Switch**: If Redis dies, Inventory returns `503` (Fail-Closed) to prevent overselling. |
| **Keycloak 26** | **HA Cluster** | Handles OIDC/OAuth 2.1. The API uses BFF token proxy. Token Introspection acts as a Kill Switch for banned users. |
| **Meilisearch** | **Multi-Node Cluster** | Sub-50ms product search. Syncs via Wolverine outbox. **SLA**: Price updates must sync in `<30s`. |
| **Storage** | **Azure Blob / AWS S3** | Media uploads. Requires presigned URL generation. CDN fronting is mandatory. |

---

## Phase 4: Database Migrations (The DBA Workflow)
*Goal: Safe, auditable schema evolution.*

> ⚠️ **CRITICAL**: Auto-migrations (`app.MigrateDatabaseAsync`) are **strictly forbidden** in production. Migrations must be generated as idempotent SQL scripts, reviewed by a DBA, and executed via a pipeline (e.g., Flyway, Liquibase, or GitHub Actions).

**Generate Idempotent Scripts for all 6 schemas:**
```bash
# 1. Catalog
dotnet ef migrations script --project src/Catalog/Catalog.Infrastructure --context CatalogDbContext --idempotent -o migrations/catalog.sql

# 2. Ordering (Includes Wolverine Outbox/Saga tables)
dotnet ef migrations script --project src/Ordering/Ordering.Infrastructure --context OrderingDbContext --idempotent -o migrations/ordering.sql

# 3. Inventory
dotnet ef migrations script --project src/Inventory/Inventory.Infrastructure --context InventoryDbContext --idempotent -o migrations/inventory.sql

# 4. Payments
dotnet ef migrations script --project src/Payments/Payments.Infrastructure --context PaymentsDbContext --idempotent -o migrations/payments.sql

# 5. Finance (Immutable Audit Logs)
dotnet ef migrations script --project src/Finance/Finance.Infrastructure --context FinanceDbContext --idempotent -o migrations/finance.sql

# 6. Shipping
dotnet ef migrations script --project src/Shipping/Shipping.Infrastructure --context ShippingDbContext --idempotent -o migrations/shipping.sql
```
*Note: Wolverine automatically manages its own outbox (`wolverine_outgoing_envelopes`) and saga state tables within the `ordering` schema. Do not manually alter these tables.*

---

## Phase 5: Financial Integrity & Observability
*Goal: Protect revenue and ensure instant incident response.*

### 5.1 Triple-Lock Reconciliation & Ghost Charge Protection
The `ReconciliationEngine` runs daily (T+1). If Stripe charges a customer but no internal order exists (a "Ghost Charge"), it triggers a `CriticalFinancialAlert`.

**Map PagerDuty for immediate on-call escalation:**
```env
Finance__Alerting__PagerDutyRoutingKey=<YOUR_PAGERDUTY_EVENTS_API_V2_KEY>
Finance__Alerting__DiscrepancyAlertThreshold=100.00 # Alert on mismatches > $100
Finance__Alerting__SendEmailAlerts=true
Finance__Alerting__FinanceAlertEmail=finance-ops@yourcompany.com
```

### 5.2 OpenTelemetry (OTLP) Routing
NetCommerce exports Traces, Metrics, and Logs natively via OTLP.
```env
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_SERVICE_NAME=netcommerce-api
```
*Ensure your collector routes to Datadog, Splunk, or Seq. The `CorrelationIdMiddleware` propagates `X-Correlation-Id` across all logs and traces.*

### 5.3 Kubernetes Probes
Configure your K8s Deployment to use the built-in Aspire health endpoints:
```yaml
livenessProbe:
  httpGet:
    path: /health/live  # alias for /health/alive
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

---

## Phase 6: CI/CD & Native AOT Deployment
*Goal: Compile to native machine code for maximum security and performance.*

> ⚡ **2026 Best Practice**: Use **Ubuntu Chiseled Extra** (`runtime-deps:10.0-noble-chiseled-extra`) for AOT (includes ICU for `InvariantGlobalization=false` / GEL). They contain no shell (`/bin/sh`), no package manager, and run as non-root, drastically reducing the CVE attack surface.

### 6.1 GitHub Actions / GitLab CI Pipeline Steps

**1. Restore & Build (Strict Lock Mode):**
```bash
dotnet restore NetCommerce.slnx --locked-mode
dotnet build NetCommerce.slnx -c Release --no-restore
```

**2. Wolverine Static Codegen (CRITICAL FOR AOT):**
Wolverine handlers must be pre-generated so the AOT compiler can see them without runtime reflection (`TypeLoadMode.Static`).
```bash
dotnet run --project src/Api/NetCommerce.Api.csproj -- codegen write
# This generates C# files in src/Api/Internal/Generated/WolverineHandlers/
```

**3. Docker Build (Multi-Stage AOT):**
```bash
docker build -t netcommerce-api:aot -f src/Api/Dockerfile .
```
*The `Dockerfile` uses `mcr.microsoft.com/dotnet/sdk:10.0-noble` → `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra` and runs as **UID 1654** (non-root). First build ~6min, cached ~30s.*

**4. Push to Registry & Deploy to K8s:**
```bash
docker push yourregistry.azurecr.io/netcommerce-api:aot
kubectl apply -f k8s/deployment.yaml
```

---

## Phase 7: Day 2 Operations & Incident Response
*Goal: Equip the Ops team with tools to recover from edge cases.*

NetCommerce provides **Admin Elevated** endpoints (protected by Role + API Key + IP Allowlist, `SecurityMode=Strict`) for manual interventions.

### 7.1 Dead Letter Queue (DLQ) Management
If a Wolverine message fails repeatedly (e.g., downstream service bug), it moves to the DLQ.
* **List DLQ**: `GET /api/admin/dlq`
* **Bulk Replay**: `POST /api/admin/dlq/bulk-replay` (Use after deploying a hotfix).

### 7.2 Saga Recovery (Stuck Orders)
If the `OrderFulfillmentSaga` gets stuck in `ManualInterventionRequired` (e.g., Stripe webhook lost, but payment verified in dashboard):
* **Force Complete**: `POST /api/admin/orders/{orderId}/force-complete`
* **Override Payment**: `POST /api/admin/orders/{orderId}/override-payment-status`

### 7.3 Manual Financial Reconciliation
If the daily T+1 job fails or you need to investigate a specific date:
* **Trigger Manual**: `POST /api/admin/finance/reconciliation-sessions/trigger`
* **Resolve Discrepancy**: `POST /api/admin/finance/discrepancies/resolve` (Actions: `RefundGhostCharge`, `CreateShadowOrder`, `AcceptDiscrepancy`).

---

## ✅ Production Go-Live Checklist

### Security & Identity
- [ ] **Keycloak**: Realm `netcommerce` configured, BFF client secrets injected.
- [ ] **Token Introspection**: `Auth__IntrospectionEnabled=true` (Kill Switch active).
- [ ] **Admin Elevated**: `Auth__AdminElevated__ApiKey` generated (≥32 chars, `Strict`), IP allowlist via `ForwardedHeaders` `KnownIPNetworks` restricted to Corporate VPN.
- [ ] **PII Vault**: KMS URI configured, AES-256-GCM encryption verified in DB.
- [ ] **CORS**: `Cors__AllowedOrigins` strictly limited to production frontend domains.
- [ ] **Security Headers**: `SecurityHeadersExtensions` (NetEscapades) active, `Kestrel AddServerHeader=false`, Scalar uses `ScalarDevUI` policy.

### Database & State
- [ ] **PgBouncer**: Deployed in transaction mode, `max_connections` 400.
- [ ] **Schemas**: 6 schemas (`catalog`, `ordering`, `inventory`, `payments`, `shipping`, `finance`) + `wolverine` created.
- [ ] **Migrations**: Idempotent SQL scripts executed by DBA.
- [ ] **Redis**: Cluster deployed. "Kill Switch" verified (API returns 503 if Redis drops).

### Financial & Compliance
- [ ] **Stripe**: Live API keys and Webhook secrets injected.
- [ ] **PagerDuty**: Routing key injected for `CriticalFinancialAlert` (Ghost Charge protection).
- [ ] **Audit Logs**: `finance.financial_audit_log` table verified as append-only.

### CI/CD & AOT
- [ ] **Wolverine Codegen**: `codegen write` step added to CI pipeline.
- [ ] **JSON Source Gen**: All new DTOs registered in `ApiJsonContext` (prevents AOT runtime crashes).
- [ ] **Docker Image**: Built using `src/Api/Dockerfile` (`10.0-noble` → `noble-chiseled-extra`), verified as chiseled (no shell `IndexOutOfRangeException` confirms native binary) and non-root.
- [ ] **CancellationToken**: `pr-validation.yml` grep guard active.

### Observability
- [ ] **OTLP**: `OTEL_EXPORTER_OTLP_ENDPOINT` routing to Datadog/Seq/Splunk.
- [ ] **Probes**: K8s Liveness (`/health/live` alias) and Readiness (`/health/ready`) configured, `CleanupJobHealthCheck` → readiness fail on 3 consecutive cleanup failures.
- [ ] **Seq/Logging**: Structured JSON logging verified, `CorrelationId` propagating.

### Testing
- [ ] **xUnit v3**: `xunit.v3 4.0.0` with `OutputType Exe`, `IAsyncLifetime` → `ValueTask`, `FsCheck` migrated to Fact loops.
- [ ] **Build**: `dotnet build -c Release` 0 warnings (NoWarn for `IL*`, `CA*`, `xUnit*`).
- [ ] **Tests**: `dotnet test NetCommerce.slnx` → `Domain 534`, `Arch 37` green (Integration 265 requires Docker).
