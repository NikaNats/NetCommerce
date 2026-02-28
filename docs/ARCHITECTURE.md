# Architecture

NetCommerce is a **Modular Monolith** built with .NET 10. Each bounded context is a self-contained module with its own domain, application, and infrastructure layers. Modules communicate exclusively through Wolverine messaging with transactional outbox guarantees.

## Design Principles

| Principle | Implementation |
|---|---|
| **Domain-Driven Design** | Aggregates, entities, value objects, domain events per bounded context |
| **Clean Architecture** | Domain at the center, no outward dependencies |
| **Modular Monolith** | Single deployable unit, isolated module boundaries enforced by architecture tests |
| **CQRS** | Commands via Wolverine handlers, queries via direct repository access |
| **Event-Driven** | Integration events via transactional outbox, saga orchestration |
| **Result Pattern** | No exceptions for business errors — `Result<T>` everywhere |

## Module Boundaries

```
┌──────────────────────────────────────────────────────────────────────────┐
│                            API Layer                                     │
│  Minimal APIs · Wolverine.Http · JSON Source Gen · Auth · Rate Limiting  │
├──────┬──────┬──────┬──────┬──────┬──────┬──────┬────────────────────────┤
│      │      │      │      │      │      │      │                        │
│ Cata │ Bask │ Orde │ Inve │ Paym │ Ship │ Medi │       Finance          │
│ log  │ et   │ ring │ ntory│ ents │ ping │ a    │                        │
│      │      │      │      │      │      │      │                        │
├──────┴──────┴──────┴──────┴──────┴──────┴──────┴────────────────────────┤
│                     Wolverine Message Bus                                │
│              Transactional Outbox · Saga Persistence                     │
├──────────────────────────────────────────────────────────────────────────┤
│                     Shared Kernel                                        │
│    Domain.Shared (Events, Money) · Kernel.Core · Kernel.Adapters        │
├──────────────────────────────────────────────────────────────────────────┤
│                     Infrastructure                                       │
│    PostgreSQL 17 · Redis 8 · MeiliSearch · Azure Blob · Stripe          │
└──────────────────────────────────────────────────────────────────────────┘
```

### Bounded Contexts

| Context | Database Schema | Responsibility |
|---|---|---|
| **Catalog** | `catalog` | Product lifecycle, categories, pricing, MeiliSearch sync |
| **Basket** | — (Redis) | Shopping basket with per-user isolation |
| **Ordering** | `ordering` | Order creation, saga orchestration, grace period |
| **Inventory** | `inventory` | Stock management, soft reservations, pessimistic locking |
| **Payments** | `payments` | Stripe integration, webhook processing, refunds, disputes |
| **Shipping** | `shipping` | Shipment tracking, courier adapter pattern |
| **Media** | — (Azure Blob) | Image upload, presigned URLs, content type validation |
| **Finance** | `finance` | Financial audit trail, T+1 reconciliation, ghost-charge detection |

## Clean Architecture Layers

Each module follows three layers with strict dependency rules:

```
                    ┌─────────────────┐
                    │   Domain Layer   │  Aggregates, Entities, Value Objects
                    │   (innermost)    │  Domain Events, Repository Interfaces
                    └────────▲────────┘
                             │ depends on
                    ┌────────┴────────┐
                    │ Application Layer│  Commands, Queries, Handlers
                    │                  │  Service Interfaces
                    └────────▲────────┘
                             │ depends on
                    ┌────────┴────────┐
                    │ Infrastructure   │  EF Core, External APIs
                    │   (outermost)    │  Repository Implementations
                    └─────────────────┘
```

**Dependency rules:**
- Domain depends only on `NetCommerce.Kernel.Core`
- Application depends on Domain and `NetCommerce.Kernel.Application`
- Infrastructure depends on Application and `NetCommerce.Kernel.EfCore`
- No layer may reference the API project directly

These rules are validated automatically by `NetCommerce.Architecture.Tests` using NetArchTest.

## Kernel Assemblies

The kernel provides shared primitives without introducing cross-module coupling:

| Assembly | Contents |
|---|---|
| `NetCommerce.Kernel.Core` | `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `Result<T>`, `Error`, `Guard`, `IStronglyTypedId<T>`, `EncryptedData`, `BlindIndex` |
| `NetCommerce.Kernel.Application` | `ICommand<T>`, `IQuery<T>`, `IRepository<T>`, `IUnitOfWork`, `ITenantContext`, `PaginatedResponse<T>` |
| `NetCommerce.Kernel.EfCore` | `BaseDbContext`, `BaseRepository<TAggregate, TId>`, `StronglyTypedIdConvention`, multi-tenancy filters |
| `NetCommerce.Kernel.AspNetCore` | `GlobalExceptionHandler`, API middleware, Kestrel hardening |
| `NetCommerce.Kernel.Compliance` | `AuditEntry`, `PiiVaultEntry`, `IEncryptionService`, audit middleware |

### Domain.Shared

`NetCommerce.Domain.Shared` contains cross-cutting types that multiple modules depend on:

- **Integration events** — `OrderSubmittedIntegrationEvent`, `StockReservedIntegrationEvent`, etc.
- **Saga messages** — Commands, events, and timeouts for the order fulfillment saga
- **Value objects** — `Money` (default currency: GEL)
- **Real-time messages** — `OrderStatusChanged` (SignalR)

## Messaging Architecture

All inter-module communication flows through Wolverine with PostgreSQL-backed transactional outbox:

```
Module A                     Wolverine                    Module B
   │                            │                            │
   │  Save Entity + Publish     │                            │
   │  (same transaction)        │                            │
   │ ──────────────────────────>│                            │
   │                            │  Outbox polls & delivers   │
   │                            │ ──────────────────────────>│
   │                            │                            │  Handle message
   │                            │                            │  (own transaction)
```

Key characteristics:
- **Transactional outbox** — messages are saved atomically with domain changes
- **At-least-once delivery** — handlers must be idempotent
- **TypeLoadMode.Static** — pre-generated handler code for Native AOT compatibility
- **Dead letter queue** — failed messages routed to DLQ with admin replay endpoints

See [MESSAGING_PATTERNS.md](MESSAGING_PATTERNS.md) for the complete saga state machine and event catalog.

## Data Architecture

### Database per Schema

Each module owns an isolated PostgreSQL schema. No cross-schema joins or foreign keys:

| Schema | Tables |
|---|---|
| `catalog` | Products, Categories, ProductImages |
| `ordering` | Orders, OrderItems, wolverine_* (saga, outbox) |
| `inventory` | Stocks, StockReservations |
| `payments` | PaymentTransactions, ProcessedWebhookEvents |
| `shipping` | Shipments, ShipmentItems |
| `finance` | FinancialAuditEntries, ReconciliationSessions, ReconciliationDiscrepancies |

### Concurrency Control

- **Optimistic concurrency** — EF Core `xmin` system column as concurrency token on all aggregates
- **Pessimistic locking** — `SELECT ... FOR UPDATE` for inventory operations under high contention

### Caching Strategy

| Layer | Technology | TTL | Purpose |
|---|---|---|---|
| L1 (in-process) | HybridCache | 5 min | Hot product data |
| L2 (distributed) | Redis | 60 min | Cross-instance consistency |
| Search | MeiliSearch | Event-driven | Full-text product search |

Cache invalidation is event-driven: product changes publish domain events that trigger cache eviction handlers.

## API Architecture

### Endpoint Organization

Endpoints are grouped by module in `src/Api/Endpoints/{Module}/` using the `IEndpoint` pattern:

```
src/Api/Endpoints/
├── Catalog/
│   ├── ProductEndpoints.cs      # /api/v{version}/products
│   ├── CategoryEndpoints.cs     # /api/v{version}/categories
│   └── SearchEndpoints.cs       # /api/v{version}/products/search
├── Ordering/
│   └── OrderEndpoints.cs        # /api/v{version}/orders
├── Basket/
│   └── BasketEndpoints.cs       # /api/v{version}/basket
├── Inventory/
│   └── InventoryEndpoints.cs    # /api/v{version}/inventory
├── Media/
│   └── MediaEndpoints.cs        # /api/v{version}/media
├── Payments/
│   └── PaymentWebhookEndpoints.cs  # /api/webhooks/stripe
├── Auth/
│   └── AuthEndpoints.cs         # /api/v{version}/auth
└── Admin/
    ├── AdminDlqEndpoints.cs           # /api/admin/dlq
    ├── AdminFinanceEndpoints.cs       # /api/admin/finance
    └── AdminOrderRecoveryEndpoints.cs # /api/admin/orders
```

### API Versioning

URL-based versioning via `Asp.Versioning.Http`:

```
/api/v1/products
/api/v1/orders
```

Admin endpoints are unversioned (`/api/admin/...`).

### Middleware Pipeline

The request pipeline processes in this order:

1. Health check endpoints (`/health/ready`)
2. Exception handler
3. Status code pages
4. Response compression (Brotli + Gzip)
5. Correlation ID middleware
6. OpenAPI (development only)
7. Enterprise web host (Kestrel hardening)
8. HTTPS redirection
9. Rate limiter
10. CORS
11. Authentication
12. Authorization
13. Zero-trust token introspection middleware
14. SignalR hub (`/api/messages`)
15. Antiforgery
16. Mapped endpoints (explicit + Wolverine.Http)

## Native AOT

The API project supports Native AOT compilation:

- **Source-generated JSON** via `ApiJsonContext` (~80+ registered types)
- **Wolverine TypeLoadMode.Static** — pre-generated handler code, no runtime reflection
- **Trim analyzers** enabled — IL2026/IL3050 warnings monitored
- **~80ms startup** and **~65MB container image** in production

See [NATIVE_AOT_VERIFICATION.md](NATIVE_AOT_VERIFICATION.md) for the 5-checkpoint verification process.

## Aspire Orchestration

The `NetCommerce.AppHost` project defines all infrastructure as code:

```csharp
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin(c => c.WithHostPort(5050))
    .WithLifetime(ContainerLifetime.Persistent);

var catalogDb = postgres.AddDatabase("CatalogDb");
var orderingDb = postgres.AddDatabase("OrderingDb");
// ... per-module databases

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisInsight()
    .WithLifetime(ContainerLifetime.Persistent);
```

All connection strings, service endpoints, and configuration values are injected into the API project automatically.

## Cross-Cutting Concerns

| Concern | Implementation |
|---|---|
| **Observability** | OpenTelemetry (ASP.NET Core, HTTP, EF Core, Redis, runtime instrumentation) → Seq |
| **Correlation** | `CorrelationIdMiddleware` propagates `X-Correlation-Id` across all requests |
| **Resilience** | Polly (retry + circuit breaker) for Stripe, Keycloak, external HTTP |
| **Rate Limiting** | Per-user and per-policy limits (`AuthStrict`, `AdminStrict`) |
| **PII Protection** | Encrypted data at rest, blind indexes for lookup, PII vault isolation |
| **Audit Trail** | `AuditMiddleware` tracks all state changes with before/after snapshots |
| **Idempotency** | `X-Idempotency-Key` header on order creation, webhook deduplication |

## Related Documentation

- [Architecture Diagrams](ARCHITECTURE_DIAGRAMS.md) — visual representations
- [Domain Model](DOMAIN_MODEL.md) — aggregate and entity details
- [Messaging Patterns](MESSAGING_PATTERNS.md) — events, sagas, outbox
- [Security](SECURITY.md) — auth, PII, rate limiting
- [API Reference](API_REFERENCE.md) — all endpoints
