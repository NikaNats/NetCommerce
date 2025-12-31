# NetCommerce - E-Commerce Modular Monolith

A production-ready e-commerce platform built with .NET 10 and .NET Aspire, following Domain-Driven Design (DDD), Clean Architecture, and the "Modular Monolith First" philosophy.

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    .NET Aspire Dashboard                     │
│         (Orchestration, Monitoring, Observability)          │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                      API Gateway                             │
│      (Keycloak JWT Auth, Versioning, Health Checks)         │
└─────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                    Modular Monolith                          │
├──────────┬──────────┬──────────┬──────────┬─────────────────┤
│ Catalog  │  Basket  │ Ordering │Inventory │    Payments     │
│  Module  │  Module  │  Module  │  Module  │     Module      │
└──────────┴──────────┴──────────┴──────────┴─────────────────┘
                              │
┌─────────────────────────────────────────────────────────────┐
│                     Shared Kernel                            │
│    (Domain Primitives, Results, Events, Abstractions)        │
└─────────────────────────────────────────────────────────────┘
                              │
┌──────────┬──────────┬──────────┬──────────┬─────────────────┐
│PostgreSQL│  Redis   │ Keycloak │  Azure   │      Seq        │
│(Per-     │(Cache/   │  (IAM)   │  Blob    │   (Logging)     │
│ Module)  │ Lock)    │          │ Storage  │                 │
└──────────┴──────────┴──────────┴──────────┴─────────────────┘
```

## 📦 Modules

| Module | Description | Database |
|--------|-------------|----------|
| **Catalog** | Products, Categories, Full-Text Search | `CatalogDb` |
| **Basket** | Shopping Cart (Redis-based) | Redis |
| **Ordering** | Orders, Price Snapshotting, Outbox | `OrderingDb` |
| **Inventory** | Stock, Soft Reservations, Redlock | `InventoryDb` |
| **Payments** | Gateway Abstraction, Ledger | `PaymentsDb` |
| **Media** | Azure Blob Upload, CDN Links | Azure Storage |

## 🚀 Quick Start

### Prerequisites
- .NET 10 SDK
- Docker Desktop (running)
- Visual Studio 2022 / VS Code

### Running with Aspire

```bash
dotnet run --project src/NetCommerce.AppHost
```

This starts via Aspire orchestration:
- **PostgreSQL** with per-module databases (CatalogDb, OrderingDb, InventoryDb, PaymentsDb)
- **pgAdmin** on port 5050
- **Redis** with RedisInsight
- **Keycloak** for authentication
- **Azurite** (Azure Blob Storage emulator)
- **Seq** for structured logging
- **Aspire Dashboard** with OpenTelemetry

Access:
- **Aspire Dashboard**: https://localhost:17225 (shown in console)
- **API**: Dynamically assigned (check dashboard)
- **Swagger**: `{API_URL}/swagger`
- **Health Check**: `{API_URL}/health/ready`

## 🔐 Authentication with Keycloak

JWT-based authentication via Keycloak with role-based access control.

### Test Users

| Email | Password | Role |
|-------|----------|------|
| admin@netcommerce.com | Admin123! | admin |
| customer@netcommerce.com | Customer123! | customer |
| vendor@netcommerce.com | Vendor123! | vendor |

### Getting a Token

Use Swagger UI's "Authorize" button or request directly:

```http
POST {KEYCLOAK_URL}/realms/netcommerce/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&client_id=netcommerce-api&username=customer@netcommerce.com&password=Customer123!
```

## 🔑 Key Features

### Non-Functional Requirements
- ✅ **API Versioning** (`/api/v1/`)
- ✅ **Idempotency Keys** (`X-Idempotency-Key` header)
- ✅ **Distributed Locking** (Redis Redlock)
- ✅ **Structured Logging** (Seq + OpenTelemetry)
- ✅ **Health Checks** (`/health/ready`, `/health/alive`)
- ✅ **Correlation IDs** (`X-Correlation-ID`)
- ✅ **Optimistic Concurrency** (Row Version)
- ✅ **Resilience** (Polly via Aspire)

### Patterns Implemented
- **CQRS** - Command Query Responsibility Segregation
- **Result Pattern** - Explicit error handling
- **Domain Events** - Loose coupling between aggregates
- **Transactional Outbox** - Guaranteed event delivery
- **Price Snapshotting** - Historical data preservation
- **Soft Reservations** - 15-minute inventory holds

## 📁 Project Structure

```
NetCommerce/
├── src/
│   ├── NetCommerce.AppHost/        # Aspire Orchestration
│   │   ├── Program.cs              # Infrastructure definition
│   │   └── realms/                 # Keycloak realm config
│   ├── NetCommerce.ServiceDefaults/# OpenTelemetry, Health, Polly
│   ├── Api/                        # ASP.NET Core Host
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   ├── Authentication/         # Keycloak JWT setup
│   │   └── Extensions/
│   ├── Shared/
│   │   ├── SharedKernel/           # Domain primitives
│   │   └── SharedKernel.Infrastructure/
│   ├── Catalog/
│   │   ├── Domain/                 # Entities, Value Objects
│   │   ├── Application/            # CQRS Handlers
│   │   └── Infrastructure/         # EF Core, Repositories
│   ├── Basket/
│   ├── Ordering/
│   ├── Inventory/
│   ├── Payments/
│   └── Media/
├── NetCommerce.sln
└── README.md
```

## 📊 Database Migrations

Each module has its own database. Run migrations:

```bash
# Catalog module
dotnet ef migrations add InitialCatalog -p src/Catalog/Infrastructure -s src/Api -c CatalogDbContext

# Apply migrations
dotnet ef database update -p src/Catalog/Infrastructure -s src/Api -c CatalogDbContext
```

## 🧪 Testing

```bash
dotnet test
```

## 📈 Scalability Roadmap

1. **Phase 1 (MVP)**: Modular Monolith with Aspire ✅
2. **Phase 2 (Growth)**: RabbitMQ, Hangfire Workers
3. **Phase 3 (Enterprise)**: Extract to Microservices, Kubernetes

## 🛠️ Technology Stack

- **.NET 10** - Latest framework
- **.NET Aspire 13.1** - Cloud-native orchestration
- **PostgreSQL** - Per-module databases
- **Redis** - Cache, Basket, Distributed Locks
- **Keycloak** - Identity & Access Management
- **Azure Blob Storage** - Object storage (Azurite locally)
- **Seq** - Structured logging & tracing
- **OpenTelemetry** - Distributed tracing & metrics
- **Polly** - Resilience & retry policies
- **MediatR** - CQRS mediator
- **FluentValidation** - Request validation
- **EF Core** - ORM with PostgreSQL

## 📝 License

MIT
