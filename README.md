# NetCommerce

A production-grade **Modular Monolith** e-commerce platform built with .NET 10, Aspire 13.1, and Wolverine 5.13. Implements Domain-Driven Design with Clean Architecture, Native AOT compilation, and enterprise-grade financial workflows.

## Architecture at a Glance

```
┌─────────────────────────────────────────────────────────────┐
│              API Gateway (Minimal APIs + Wolverine.Http)     │
├──────┬──────┬──────┬──────┬──────┬──────┬──────┬───────────┤
│Catalog│Basket│Order │Inven │Pay   │Ship  │Media │Finance    │
│      │      │ing   │tory  │ments │ping  │      │           │
├──────┴──────┴──────┴──────┴──────┴──────┴──────┴───────────┤
│              Wolverine Message Bus (Transactional Outbox)    │
├─────────────────────────────────────────────────────────────┤
│    PostgreSQL 17    │   Redis 8   │  MeiliSearch  │ Azurite │
└─────────────────────────────────────────────────────────────┘
```

**8 bounded contexts**, each with its own database schema, communicating via asynchronous messaging with guaranteed delivery.

## Key Capabilities

| Capability | Implementation |
|---|---|
| **Order Fulfillment** | 10-state saga with compensation, timeouts, and manual intervention |
| **Inventory** | Soft reservation → Lock → Confirm pattern with pessimistic locking |
| **Payments** | Stripe webhook-first integration with idempotent processing |
| **Financial Integrity** | T+1 reconciliation engine with ghost-charge detection |
| **Search** | MeiliSearch with faceted search and CQRS read-model projection |
| **Auth** | Keycloak OIDC with zero-trust token introspection and RBAC |
| **Native AOT** | Source-generated JSON, ~80ms startup, ~65MB container image |
| **Observability** | OpenTelemetry + Seq + Serilog structured logging |

## Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Runtime | .NET | 10.0.100 |
| Orchestration | .NET Aspire | 13.1.0 |
| Messaging | Wolverine | 5.13.0 |
| ORM | EF Core + Npgsql | 10.0.2 |
| Database | PostgreSQL | 17 |
| Cache | Redis (HybridCache L2) | 8 |
| Search | MeiliSearch | 0.18.0 |
| Payments | Stripe.net | 50.4.0-beta.1 |
| Auth | Keycloak | OIDC + RBAC |
| Blob Storage | Azure Blob / Azurite | 12.26.0 |
| API Docs | Scalar (OpenAPI) | 2.12.30 |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100+)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for Aspire-managed infrastructure)

### Run with Aspire

```powershell
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

Aspire automatically provisions:
- **PostgreSQL 17** with PgAdmin (port 5050) — 6 isolated schemas
- **Redis 8** with RedisInsight
- **Keycloak** with pre-configured `netcommerce` realm
- **Seq** for structured log aggregation
- **MeiliSearch** for product search
- **Azurite** for blob storage emulation

### Run Tests

```powershell
# All tests (608 tests across 5 projects)
dotnet test NetCommerce.slnx -v minimal --nologo

# Architecture boundary tests only
dotnet test tests/NetCommerce.Architecture.Tests --nologo

# Domain unit tests only
dotnet test tests/NetCommerce.Domain.Tests --nologo
```

See [GETTING_STARTED.md](docs/GETTING_STARTED.md) for detailed setup and [TESTING.md](docs/TESTING.md) for the full test strategy.

## Project Structure

```
src/
├── Api/                          # Minimal API endpoints, middleware, JSON source generation
│   └── Endpoints/                # Grouped by module (Catalog, Ordering, Basket, etc.)
├── Catalog/                      # Product catalog bounded context
│   ├── Catalog.Application/      # Commands, queries, handlers
│   ├── Catalog.Domain/           # Product, Category aggregates
│   └── Catalog.Infrastructure/   # EF Core, MeiliSearch sync
├── Ordering/                     # Order management + saga orchestration
├── Inventory/                    # Stock management with reservation pattern
├── Payments/                     # Stripe webhook processing + reconciliation
├── Shipping/                     # Shipment tracking with courier adapters
├── Basket/                       # Redis-backed shopping basket
├── Media/                        # Azure Blob media management
├── Finance/                      # Financial audit, reconciliation engine
├── Domain.Shared/                # Integration events, saga messages, Money VO
├── Kernel/                       # Core primitives (Entity, AggregateRoot, Result, Guard)
├── Kernel.Adapters/              # EF Core, ASP.NET Core, Compliance adapters
├── NetCommerce.AppHost/          # Aspire orchestration
└── NetCommerce.ServiceDefaults/  # OpenTelemetry, health checks, resilience
tests/
├── NetCommerce.Architecture.Tests/   # Clean Architecture boundary validation
├── NetCommerce.Domain.Tests/         # Unit tests (536 tests)
├── NetCommerce.Integration.Tests/    # Testcontainers-based integration tests
├── NetCommerce.LoadTests/            # NBomber load/stress tests
└── NetCommerce.AppHost.Tests/        # Aspire topology tests
```

## Documentation

| Document | Description |
|---|---|
| [Getting Started](docs/GETTING_STARTED.md) | Prerequisites, setup, first run |
| [Architecture](docs/ARCHITECTURE.md) | Modular monolith design, Clean Architecture layers |
| [Architecture Diagrams](docs/ARCHITECTURE_DIAGRAMS.md) | Visual system diagrams |
| [Domain Model](docs/DOMAIN_MODEL.md) | Aggregates, entities, value objects per context |
| [Messaging Patterns](docs/MESSAGING_PATTERNS.md) | Wolverine messaging, saga state machine, events |
| [API Reference](docs/API_REFERENCE.md) | All REST endpoints with parameters |
| [Webhook Reference](docs/WEBHOOK_REFERENCE.md) | Stripe webhook processing |
| [Testing](docs/TESTING.md) | Test strategy, fixtures, categories |
| [Deployment](docs/DEPLOYMENT.md) | Docker, Native AOT, CI/CD |
| [Operations](docs/OPERATIONS.md) | Health checks, monitoring, DLQ management |
| [Security](docs/SECURITY.md) | Auth, PII isolation, rate limiting |
| [Financial Integrity](docs/FINANCIAL_INTEGRITY_MATRIX.md) | Reconciliation, audit trail |
| [Native AOT](docs/NATIVE_AOT_VERIFICATION.md) | AOT verification checkpoints |
| [Inventory Patterns](docs/INVENTORY_PATTERNS.md) | Reservation lifecycle, contention handling |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Common issues and resolution |
| [Contributing](docs/CONTRIBUTING.md) | Development workflow, conventions |
| [Changelog](docs/CHANGELOG.md) | Version history |

## Build Configuration

The solution enforces strict quality standards:

- **TreatWarningsAsErrors** in Release and CI builds
- **Central Package Management** with transitive pinning
- **NuGet Audit** at moderate level for vulnerability scanning
- **Deterministic builds** with reproducible outputs
- **Native AOT analyzers** (IL2026, IL3050) in non-test projects
- **Control Flow Guard** for binary hardening

## License

See [LICENSE](LICENSE) for details.
