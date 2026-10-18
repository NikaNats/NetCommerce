# Changelog

All notable changes to NetCommerce, organized by development phase.

## Release Hardening — Inventory Concurrency

**Status:** Complete | Found by the weekly load suite, which had been red since 2026-10-11

### Fixed
- **Oversell in checkout reservations (critical).** `ReserveInventoryHandler` (the saga path every checkout takes) loaded reservations in the same statement as `SELECT ... FOR UPDATE`. Under READ COMMITTED, a waiter that acquired the lock saw the fresh stock row but a stale reservation set, so concurrent orders for one product over-allocated (40 of 40 reservations succeeded against 10 units). All stock locks now go through `StockRowLocks.LockAsync` in their own statement, with the aggregate loaded afterwards. The same fix applies to payment-lock and confirm, plus `StockRepository.GetByProductIdForUpdateAsync`.
- **Lost reservations on `POST /api/v1/inventory/reserve`.** `ReserveStockHandler` took its row lock outside any transaction, so the lock was released immediately and concurrent reservers failed with `409` on `xmin` collisions (30 of 5,000 succeeded against ample stock). It now runs in an execution-strategy transaction, clears the change tracker per attempt (a transient retry could otherwise persist a duplicate reservation), and shares the lock-then-load rule.
- Reservation loads for reserving now fetch only non-terminal reservations; unpruned Confirmed/Released history no longer slows every reservation of a popular product.

### Tests
- New `PartitionedStockHandlerTests.*_ConcurrentOrdersForOneProduct_ShouldNeverOversell` (real PostgreSQL, 40 concurrent reservers, 10 units) for both reserve paths. Both failed before the fix.
- Single-key saturation now asserts zero lost reservations and zero failed requests. Its P99/P50 ratio gate, which flagged orderly FIFO tail latency and passed while 99% of requests failed fast, is replaced by a head-of-queue stall bound. Warm-up now exercises the real reserve path.
- Load suites now capture console output (`[assembly: CaptureConsole]`), so CI logs show each suite's metrics.
- Weekly saturation burst resized from 5,000 to 1,500 requests per 10s to fit measured single-row capacity (see `INVENTORY_PATTERNS.md` → Hot-Product Capacity).

---

## Phase 10 — Documentation

**Status:** Complete

### Added
- Complete 18-document documentation suite covering all aspects of the project
- Root README with tech stack, quick start, project structure, and documentation index
- Architecture documentation with diagrams, domain model, and messaging patterns
- API reference covering all 56 endpoints across 12 endpoint groups
- Webhook reference with Stripe integration details
- Security documentation covering zero-trust auth, PII vault, rate limiting
- Financial integrity matrix documenting reconciliation engine and audit trail
- Native AOT verification guide with 5-checkpoint protocol
- Inventory patterns documenting reservation lifecycle and concurrency control
- Operations guide with DLQ management, reconciliation, and incident response
- Deployment guide with JIT/AOT comparison, Docker builds, and configuration
- Testing guide covering all 5 test projects and 608 tests
- Troubleshooting guide for common development and production issues
- Getting started, contributing, and developer workflow guides

### Removed
- 19 legacy draft documentation files superseded by new documentation suite

---

## Phase 9 — Final Corrections for Native AOT Compliance

**Status:** Complete | **Build Warnings:** 14 (down from 16)

### Fixed
- Admin endpoint 404 "black hole" — refactored `AdminFinanceEndpoints` and `AdminOrderRecoveryEndpoints` from `ControllerBase` to `IEndpointGroup` Minimal API pattern
- Wolverine `TypeLoadMode.Static` configuration for deterministic AOT handler loading
- Dockerfile `codegen write` step to pre-generate Wolverine handlers during build
- Added 12 DTOs to `ApiJsonContext` for admin endpoint JSON source generation

### Changed
- `AdminFinanceEndpoints`: 5 endpoints migrated to static Minimal API handlers
- `AdminOrderRecoveryEndpoints`: 6 endpoints migrated to static Minimal API handlers

---

## Financial Flow Hardening

**Status:** Complete | **Tests:** 608 passing (536 domain + 36 architecture + 36 integration)

### Fixed
- `PaymentWebhookEndpoints.HandleStripeWebhook` visibility changed from `private static` to `internal static` (allowing test access)
- `MultiTenancyExtensions` — fixed `AddMultiTenancyFilters` to use correct generic constraint
- `PartitionedStockHandlers` — fixed `ReserveInventoryBatchHandler` and `ConfirmInventoryBatchHandler` SQL queries to include `xmin` for optimistic concurrency
- `SagaPaymentHandlers.HandlePaymentTimeout` — fixed return type to match Wolverine handler convention
- `WolverineMessagingExtensions` — fixed `TypeLoadMode.Static` configuration reference
- `Program.cs` — removed duplicate `AddSignalR()` registration
- `FinancialAuditEntryConfiguration` — removed invalid `UseXminAsConcurrencyToken()` on non-aggregate entity
- `StockCommandHandlers` — fixed `FOR UPDATE` SQL to include `xmin` column
- `IntegrationTestFixture` — disabled `ReservationCleanupJob` during integration tests to prevent test interference

### Added
- `PaymentWebhookContractTests` — webhook idempotency and signature verification tests
- `ConcurrentInventoryStressTests` — concurrent reservation stress tests with FsCheck and NBomber

---

## Phase 6 — SharedKernel Complete Removal

**Status:** Complete | **Native AOT:** 0 IL2026 warnings

### Removed
- All legacy `SharedKernel` directories and assemblies
- Legacy type resolution infrastructure (type forwarders, assembly-level attributes)
- `SharedKernel.Domain`, `SharedKernel.Events`, `SharedKernel.Infrastructure` namespaces

### Changed
- All extension methods migrated to canonical `NetCommerce.Kernel.*` locations:
  - `KestrelExtensions` → `NetCommerce.Kernel.AspNetCore`
  - `WolverineMessagingExtensions` → `NetCommerce.Kernel.AspNetCore`
  - `MultiTenancyExtensions` → `NetCommerce.Kernel.EfCore`
- All types use canonical namespaces:
  - `NetCommerce.Domain.Shared.Money` (was `NetCommerce.SharedKernel.Domain.Money`)
  - `NetCommerce.Domain.Shared.Events.*` (was `NetCommerce.SharedKernel.Events.*`)

### Performance
- Startup time: -9.5%
- Binary size: -3.6%

---

## Phase 5 — Kernel Migration and SharedKernel Deprecation

**Status:** Complete

### Added
- `ITenantContext` interface in `NetCommerce.Kernel.Application`
- `HttpTenantContext` implementation in `NetCommerce.Kernel.Security`
- `NetCommerce.Domain.Shared` assembly for cross-module shared types
- Serialization migration guide for Wolverine outbox compatibility

### Changed
- `BaseDbContext` multi-tenancy filters now use `ITenantContext` interface
- All shared types moved to canonical `NetCommerce.Domain.Shared` namespace
- Money, PriceBreakdown, integration events, saga messages in Domain.Shared

### Deprecated
- All `SharedKernel.*` types marked `[Obsolete]` with migration guidance
- Legacy type locations retained as forwarding shims (removed in Phase 6)

---

## Initial Architecture

### Core
- .NET 10 Modular Monolith with Clean Architecture and DDD
- 9 bounded contexts: Catalog, Ordering, Inventory, Payments, Shipping, Media, Basket, Finance, Auth
- Strongly typed IDs with `IStronglyTypedId<T>` convention
- `Result<T>` pattern for business error handling (no exceptions)
- `Money` value object with GEL default currency

### Messaging
- Wolverine in-process message bus with transactional outbox
- `OrderFulfillmentSaga` — 10-state saga state machine coordinating order lifecycle
- Integration events for cross-module communication
- Domain events for intra-module side effects
- Dead letter queue with admin replay endpoints

### Data
- PostgreSQL 17 with 6 isolated schemas (catalog, ordering, inventory, payments, shipping, finance)
- EF Core 10 with `BaseDbContext`, `BaseRepository<T, TId>`, `StronglyTypedIdConvention`
- Redis 8 for basket storage and HybridCache L2
- MeiliSearch for product search with facets
- `xmin` optimistic concurrency across all aggregates

### Security
- Keycloak OIDC with zero-trust token validation
- 5 RBAC policies (AdminOnly, VendorOnly, CustomerOnly, OwnerOnly, AdminElevated)
- PII vault with AES-256-GCM encryption and blind indexes
- 5 rate limiting policies (Global, AuthStrict, Webhook, PerUser, AdminStrict)
- Enterprise Kestrel hardening (no server header, HTTP/3, request limits)

### Payments
- Stripe webhook-first payment processing with signature verification
- Webhook idempotency via `ProcessedWebhookEvent` deduplication
- Polly resilience policies for Stripe API calls

### Inventory
- Soft reservation model with 15-minute TTL
- Pessimistic locking (`SELECT ... FOR UPDATE`) with deterministic ordering
- Two-pass validate-then-reserve for atomic multi-item operations
- `ReservationCleanupJob` background service for leaked reservation recovery

### Finance
- T+1 daily reconciliation engine (internal vs external vs audit)
- Ghost charge detection via right-outer-join comparison
- Immutable `FinancialAuditEntry` append-only audit trail (SOX, PCI-DSS)
- `CriticalFinancialAlert` domain events for PagerDuty integration

### Infrastructure
- .NET Aspire 13.1 for local development orchestration
- Native AOT support with JSON source generation and Wolverine static codegen
- SignalR real-time notifications via Wolverine
- OpenTelemetry traces, metrics, and structured logging to Seq
- Idempotency filter for order creation endpoints
