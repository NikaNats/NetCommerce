# NetCommerce Architecture Guide

> **Comprehensive technical architecture documentation for the NetCommerce Modular Monolith**

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Architectural Principles](#architectural-principles)
3. [System Overview](#system-overview)
4. [Bounded Contexts](#bounded-contexts)
5. [Clean Architecture Layers](#clean-architecture-layers)
6. [Domain-Driven Design Implementation](#domain-driven-design-implementation)
7. [Kernel Infrastructure](#kernel-infrastructure)
8. [Data Architecture](#data-architecture)
9. [Messaging Architecture](#messaging-architecture)
10. [Security Architecture](#security-architecture)
11. [Observability Architecture](#observability-architecture)
12. [Scalability Strategy](#scalability-strategy)

---

## Executive Summary

NetCommerce is a **production-grade e-commerce platform** built as a **Modular Monolith** using .NET 10 and .NET Aspire 13.1. The architecture follows the "Modular Monolith First" strategy, allowing the system to be deployed as a single unit while maintaining strict module boundaries that enable future microservices extraction if scaling requirements demand it.

### Key Architectural Decisions

| Decision | Rationale |
|----------|-----------|
| **Modular Monolith** | Simplifies deployment, debugging, and transactions while maintaining bounded context separation |
| **Database-per-Module** | Each module owns its schema, enabling independent evolution and future extraction |
| **Wolverine Messaging** | Provides transactional outbox, saga orchestration, and durable messaging |
| **Clean Architecture** | Enforces dependency inversion, keeping domain logic isolated from infrastructure |
| **Strongly Typed IDs** | Prevents primitive obsession and provides compile-time type safety |
| **Result Pattern** | Eliminates exception-based flow control for business errors |

---

## Architectural Principles

### 1. Domain-Centric Design

The domain model is the heart of the system. All business logic resides in the Domain layer, which has **zero dependencies** on infrastructure or frameworks.

```
┌─────────────────────────────────────────────────────────────────┐
│                         API Layer                                │
│              (Minimal APIs, Controllers, Filters)               │
├─────────────────────────────────────────────────────────────────┤
│                      Application Layer                           │
│           (Commands, Queries, Handlers, Sagas)                  │
├─────────────────────────────────────────────────────────────────┤
│                        Domain Layer                              │
│     (Aggregates, Entities, Value Objects, Domain Events)        │
├─────────────────────────────────────────────────────────────────┤
│                    Infrastructure Layer                          │
│        (EF Core, Redis, External APIs, File Storage)            │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │ Dependencies point inward
                              │ (Dependency Inversion)
```

### 2. Explicit Over Implicit

- **No magic strings**: Strongly typed IDs, configuration objects, and error codes
- **No hidden control flow**: Result pattern instead of exceptions for expected failures
- **No ambient state**: All dependencies explicitly injected

### 3. Fail-Safe by Default

- **Transactional Outbox**: Messages are persisted atomically with domain changes
- **Idempotency**: Critical operations can be safely retried
- **Compensating Transactions**: Sagas implement rollback logic for all failure scenarios

### 4. Observable by Default

- **OpenTelemetry Integration**: Distributed tracing across all operations
- **Structured Logging**: Machine-readable logs with correlation IDs
- **Health Checks**: Liveness and readiness probes for orchestration

---

## System Overview

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              .NET Aspire Orchestration                       │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        NetCommerce.Api                               │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐       │   │
│  │  │ Catalog │ │ Basket  │ │Ordering │ │Inventory│ │Payments │       │   │
│  │  │ Module  │ │ Module  │ │ Module  │ │ Module  │ │ Module  │ ...   │   │
│  │  └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘ └────┬────┘       │   │
│  │       │           │           │           │           │             │   │
│  │       └───────────┴─────┬─────┴───────────┴───────────┘             │   │
│  │                         │                                           │   │
│  │              ┌──────────▼──────────┐                               │   │
│  │              │   Wolverine Bus     │                               │   │
│  │              │ (In-Process + Outbox)│                              │   │
│  │              └─────────────────────┘                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│  ┌─────────────────────────────────┼───────────────────────────────────┐   │
│  │                    Infrastructure Services                           │   │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │   │
│  │  │PostgreSQL│ │  Redis   │ │ Keycloak │ │   Seq    │ │Meilisearch│ │   │
│  │  │(per-module)│ │ (Cache)  │ │  (IAM)   │ │(Logging) │ │ (Search)  │ │   │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Module Communication

Modules communicate through two mechanisms:

1. **Synchronous Queries**: Read operations via direct service calls (same process)
2. **Asynchronous Commands/Events**: Write operations via Wolverine messaging with transactional outbox

```
┌──────────────┐    Integration Event    ┌──────────────┐
│   Ordering   │ ───────────────────────▶│  Inventory   │
│    Module    │   OrderSubmittedEvent   │    Module    │
└──────────────┘                         └──────────────┘
       │                                        │
       │ Same Transaction                       │ Same Transaction
       ▼                                        ▼
┌──────────────┐                         ┌──────────────┐
│ OrderingDb   │                         │ InventoryDb  │
│   + Outbox   │                         │   + Inbox    │
└──────────────┘                         └──────────────┘
```

---

## Bounded Contexts

### Module Boundaries

| Module | Responsibility | Key Aggregates | Storage |
|--------|---------------|----------------|---------|
| **Catalog** | Product information, categories, search | `Product`, `Category` | PostgreSQL + Meilisearch |
| **Basket** | Temporary shopping cart | `ShoppingBasket` | Redis |
| **Ordering** | Order lifecycle, fulfillment orchestration | `Order` | PostgreSQL |
| **Inventory** | Stock levels, reservations, alerts | `Stock` | PostgreSQL + Redis (locks) |
| **Payments** | Payment processing, refunds, ledger | `PaymentTransaction` | PostgreSQL |
| **Shipping** | Courier integration, tracking | `Shipment` | PostgreSQL |
| **Finance** | Reconciliation, reporting | `ReconciliationSession` | PostgreSQL |
| **Media** | File uploads, CDN management | N/A (stateless) | Azure Blob Storage |

### Context Map

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            UPSTREAM CONTEXTS                                 │
│                                                                             │
│    ┌──────────────┐                              ┌──────────────┐          │
│    │   Catalog    │◀─────Published Language─────▶│    Media     │          │
│    │ (Conformist) │        (Product Images)      │  (Supplier)  │          │
│    └──────────────┘                              └──────────────┘          │
│           │                                                                 │
│           │ Product Info                                                    │
│           ▼                                                                 │
│    ┌──────────────┐        ┌──────────────┐        ┌──────────────┐       │
│    │   Basket     │───────▶│   Ordering   │───────▶│  Inventory   │       │
│    │ (Customer)   │ Items  │ (Partnership)│ Reserve│ (Conformist) │       │
│    └──────────────┘        └──────────────┘        └──────────────┘       │
│                                   │                        │               │
│                                   │ Payment Request        │ Confirm      │
│                                   ▼                        │               │
│                            ┌──────────────┐                │               │
│                            │   Payments   │◀───────────────┘               │
│                            │  (Supplier)  │                                │
│                            └──────────────┘                                │
│                                   │                                        │
│                                   │ Reconcile                              │
│                                   ▼                                        │
│    ┌──────────────┐        ┌──────────────┐                               │
│    │   Shipping   │◀───────│   Finance    │                               │
│    │ (Conformist) │ Ship   │  (Analyst)   │                               │
│    └──────────────┘        └──────────────┘                               │
│                                                                             │
│                            DOWNSTREAM CONTEXTS                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Clean Architecture Layers

### Layer Responsibilities

#### Domain Layer (`{Module}.Domain`)

The innermost layer containing business logic with **zero external dependencies**.

```csharp
// Example: Ordering.Domain/Orders/Order.cs
public sealed class Order : AggregateRoot<Guid>
{
    public string OrderNumber { get; private set; }
    public OrderStatus Status { get; private set; }
    public Money TotalAmount { get; private set; }

    // Rich domain behavior - not anemic!
    public Result ConfirmPayment(string transactionId)
    {
        if (Status != OrderStatus.Submitted)
            return Result.Failure(Error.Validation("Order must be submitted"));

        Status = OrderStatus.Paid;
        PaymentTransactionId = transactionId;
        PaidAt = DateTime.UtcNow;

        RaiseDomainEvent(new OrderPaidDomainEvent(Id, transactionId));
        return Result.Success();
    }
}
```

**Contains:**
- Aggregates and Entities
- Value Objects
- Domain Events
- Domain Services (pure business logic)
- Repository Interfaces (abstractions only)

#### Application Layer (`{Module}.Application`)

Orchestrates use cases by coordinating domain objects and infrastructure.

```csharp
// Example: Ordering.Application/Orders/Commands/ConfirmOrderPaymentHandler.cs
[WolverineHandler]
public static class ConfirmOrderPaymentHandler
{
    public static async Task<Result> Handle(
        ConfirmOrderPaymentCommand command,
        IOrderRepository repository,
        ILogger logger)
    {
        var order = await repository.GetByIdAsync(command.OrderId);
        if (order is null)
            return Result.Failure(Error.NotFound("Order", command.OrderId));

        var result = order.ConfirmPayment(command.TransactionId);
        if (result.IsFailure)
            return result;

        await repository.UpdateAsync(order);

        logger.LogInformation("Payment confirmed for Order {OrderId}", command.OrderId);
        return Result.Success();
    }
}
```

**Contains:**
- Command/Query Handlers (Wolverine)
- Sagas (Process Managers)
- Application Services
- DTOs and Contracts

#### Infrastructure Layer (`{Module}.Infrastructure`)

Implements abstractions defined in Domain and Application layers.

```csharp
// Example: Ordering.Infrastructure/Persistence/OrderRepository.cs
public sealed class OrderRepository : BaseRepository<Order, Guid>, IOrderRepository
{
    public OrderRepository(OrderingDbContext context) : base(context) { }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber)
    {
        return await DbSet
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
    }
}
```

**Contains:**
- EF Core DbContext and Configurations
- Repository Implementations
- External Service Integrations
- Background Jobs

---

## Domain-Driven Design Implementation

### Strongly Typed IDs

All entity identifiers use the `IStronglyTypedId<T>` pattern:

```csharp
public readonly record struct OrderId(Guid Value) : IStronglyTypedId<OrderId>
{
    public static OrderId Create(Guid value) => new(value);
    public static OrderId New() => new(Guid.NewGuid());
    public static OrderId Empty => new(Guid.Empty);

    // IParsable<T> implementation for model binding
    public static OrderId Parse(string s, IFormatProvider? provider)
        => new(Guid.Parse(s));
    public static bool TryParse(string? s, IFormatProvider? provider, out OrderId result)
    {
        if (Guid.TryParse(s, out var guid))
        {
            result = new OrderId(guid);
            return true;
        }
        result = Empty;
        return false;
    }
}
```

**Benefits:**
- Compile-time type safety (can't pass `ProductId` where `OrderId` is expected)
- Self-documenting code
- Automatic EF Core converter registration via convention

### Value Objects

Immutable objects defined by their attributes, not identity:

```csharp
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public static Money Create(decimal amount, string currency = "GEL")
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative");
        return new Money(Math.Round(amount, 2), currency.ToUpperInvariant());
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

### Aggregate Root Pattern

Aggregates enforce invariants and serve as transactional boundaries:

```csharp
public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    /// <summary>Row version for optimistic concurrency.</summary>
    public uint Version { get; protected set; }

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        AddDomainEvent(domainEvent);
    }
}
```

**Rules:**
1. Only aggregate roots have repositories
2. References between aggregates use IDs, not object references
3. Transactions should not span multiple aggregates
4. Domain events enable cross-aggregate consistency

### Result Pattern

Explicit error handling without exceptions:

```csharp
public class Result<TValue> : Result
{
    public TValue Value { get; }

    public static implicit operator Result<TValue>(TValue value)
        => new(value, true, Error.None);
}

public sealed record Error(string Code, string Description, int StatusCode = 500)
{
    public static Error NotFound(string entity, object id)
        => new($"{entity}.NotFound", $"{entity} with id '{id}' not found", 404);

    public static Error Validation(string description)
        => new("Validation.Error", description, 422);

    public static Error Conflict(string description)
        => new("Conflict.Error", description, 409);
}

// Usage in handlers
public static async Task<Result<OrderDto>> Handle(GetOrderQuery query, ...)
{
    var order = await repository.GetByIdAsync(query.OrderId);
    return order is null
        ? Result.Failure<OrderDto>(Error.NotFound("Order", query.OrderId))
        : Result.Success(order.ToDto());
}
```

---

## Kernel Infrastructure

The `NetCommerce.Kernel.*` assemblies provide shared infrastructure:

### NetCommerce.Kernel.Core

Domain primitives and abstractions:

```
NetCommerce.Kernel.Core/
├── Domain/
│   ├── Entity.cs              # Base entity with domain events
│   ├── AggregateRoot.cs       # Aggregate root with concurrency
│   ├── ValueObject.cs         # Value object base class
│   └── IDomainEvent.cs        # Domain event marker interface
├── Ids/
│   └── IStronglyTypedId.cs    # Strongly typed ID contract
├── Results/
│   ├── Result.cs              # Result pattern implementation
│   └── Error.cs               # Error representation
└── Application/
    └── IAuditableCommand.cs   # Audit marker interface
```

### NetCommerce.Kernel.EfCore

Entity Framework Core infrastructure:

```
NetCommerce.Kernel.EfCore/
├── Persistence/
│   ├── BaseDbContext.cs       # Pre-configured DbContext
│   ├── BaseRepository.cs      # Generic repository
│   └── StronglyTypedIdConvention.cs  # Auto ID converter registration
└── Interceptors/
    └── DomainEventInterceptor.cs  # Publishes domain events on SaveChanges
```

### NetCommerce.Kernel.Wolverine

Messaging infrastructure:

```
NetCommerce.Kernel.Wolverine/
├── WolverineKernelExtensions.cs  # Production-ready defaults
├── Middleware/
│   └── AuditMiddleware.cs     # Command auditing
└── Serialization/
    └── LegacyTypeResolver.cs  # Migration support
```

---

## Data Architecture

### Database-per-Module Strategy

Each bounded context owns its PostgreSQL schema:

```sql
-- Catalog schema
CREATE SCHEMA catalog;
CREATE TABLE catalog.products (...);
CREATE TABLE catalog.categories (...);

-- Ordering schema
CREATE SCHEMA ordering;
CREATE TABLE ordering.orders (...);
CREATE TABLE ordering.order_items (...);

-- Wolverine infrastructure (shared)
CREATE SCHEMA wolverine;
CREATE TABLE wolverine.wolverine_incoming_envelopes (...);
CREATE TABLE wolverine.wolverine_outgoing_envelopes (...);
CREATE TABLE wolverine.saga_state (...);
```

### Caching Strategy

| Data Type | Cache Location | TTL | Invalidation |
|-----------|---------------|-----|--------------|
| Product catalog | Redis + Meilisearch | 5 min | Domain event |
| User sessions | Redis | 30 min | Sliding |
| Shopping baskets | Redis | 7 days | Explicit |
| Stock locks | Redis (RedLock) | 30 sec | Auto-expire |
| Idempotency keys | Redis | 24 hours | Auto-expire |

### Search Architecture

Meilisearch provides the product search read model:

```
┌──────────────┐    ProductCreated    ┌───────────────────┐
│   Catalog    │ ───────────────────▶│ SearchProjection  │
│   (Writes)   │    ProductUpdated    │    Handler        │
└──────────────┘                      └─────────┬─────────┘
                                                │
                                                ▼
                                      ┌───────────────────┐
                                      │   Meilisearch     │
                                      │   (Read Model)    │
                                      └───────────────────┘
                                                │
                                                ▼
                                      ┌───────────────────┐
                                      │   Search API      │
                                      │   < 50ms p99      │
                                      └───────────────────┘
```

---

## Messaging Architecture

### Wolverine Configuration

```csharp
opts.ConfigureKernelDefaults<OrderingDbContext>();

// Key settings:
// - Transactional Outbox: Messages saved with domain changes
// - Durable Inbox: At-least-once delivery guarantee
// - Dead Letter Queue: 30-day retention for audit
// - Message Identity: IdAndDestination (multi-handler safe)
```

### Message Flow

```
┌──────────────────────────────────────────────────────────────────────────┐
│                           PRODUCER (Ordering)                             │
│                                                                          │
│  1. Order.Create()                                                       │
│  2. RaiseDomainEvent(OrderSubmittedDomainEvent)                         │
│  3. SaveChangesAsync() ─────┐                                           │
│                             │ SAME TRANSACTION                          │
│                             ▼                                           │
│  ┌─────────────────────────────────────────┐                            │
│  │ OrderingDb                              │                            │
│  │ ├── orders table (domain data)         │                            │
│  │ └── wolverine_outgoing_envelopes       │◀── Integration Event       │
│  │     (outbox)                           │                            │
│  └─────────────────────────────────────────┘                            │
└──────────────────────────────────────────────────────────────────────────┘
                                │
                                │ Wolverine Agent polls outbox
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                           CONSUMER (Inventory)                            │
│                                                                          │
│  ┌─────────────────────────────────────────┐                            │
│  │ InventoryDb                             │                            │
│  │ ├── wolverine_incoming_envelopes       │◀── Deduplication check     │
│  │ │   (inbox)                            │                            │
│  │ └── stock_reservations (domain data)   │◀── Handler processes       │
│  └─────────────────────────────────────────┘                            │
│                                                                          │
│  ReserveStockHandler.Handle(OrderSubmittedIntegrationEvent)             │
└──────────────────────────────────────────────────────────────────────────┘
```

### Saga Pattern (Order Fulfillment)

```
                              ┌─────────────────┐
                              │   NotStarted    │
                              └────────┬────────┘
                                       │ StartOrderFulfillmentCommand
                                       ▼
                              ┌─────────────────┐
                              │   Reserving     │──── ReserveInventoryCommand
                              │   Inventory     │
                              └────────┬────────┘
                                       │ InventoryReserved
                                       ▼
                              ┌─────────────────┐
                              │   Processing    │──── ProcessPaymentCommand
                              │   Payment       │
                              └────────┬────────┘
                          ┌────────────┴────────────┐
                          │                         │
                   PaymentSucceeded            PaymentFailed
                          ▼                         ▼
                 ┌─────────────────┐      ┌─────────────────┐
                 │   Confirming    │      │   Compensating  │
                 │   Inventory     │      │   (Release)     │
                 └────────┬────────┘      └────────┬────────┘
                          │                        │
                 InventoryConfirmed         ResourcesReleased
                          ▼                        ▼
                 ┌─────────────────┐      ┌─────────────────┐
                 │   Completed     │      │     Failed      │
                 │ (Saga Deleted)  │      │ (With Reason)   │
                 └─────────────────┘      └─────────────────┘
```

---

## Security Architecture

### Zero-Trust Identity Mesh

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         KEYCLOAK (Identity Provider)                     │
│                                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                    │
│  │  Customer   │  │   Vendor    │  │    Admin    │                    │
│  │    Realm    │  │    Realm    │  │    Realm    │                    │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘                    │
│         │                │                │                            │
│         └────────────────┼────────────────┘                            │
│                          │                                             │
│                   ┌──────▼──────┐                                      │
│                   │   Tokens    │                                      │
│                   │  (JWT/OIDC) │                                      │
│                   └──────┬──────┘                                      │
└──────────────────────────┼──────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         NetCommerce API                                  │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    Security Pipeline                             │   │
│  │                                                                  │   │
│  │  1. Token Validation ──▶ 2. Token Introspection ──▶ 3. RBAC    │   │
│  │     (JWT Signature)       (Revocation Check)        (Policies)  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  Features:                                                              │
│  • Token Exchange (RFC 8693) for service-to-service                    │
│  • Instant revocation via introspection                                │
│  • Fine-grained authorization policies                                 │
│  • PII encryption at rest (AES-256-GCM)                               │
└─────────────────────────────────────────────────────────────────────────┘
```

### Authorization Policies

```csharp
// Role-based policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("VendorAccess", policy => policy.RequireRole("vendor", "admin"));
    options.AddPolicy("CustomerAccess", policy => policy.RequireRole("customer", "admin"));
});

// Endpoint protection
app.MapPost("/api/v1/products", CreateProduct)
   .RequireAuthorization("VendorAccess");
```

---

## Observability Architecture

### OpenTelemetry Integration

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Application Telemetry                            │
│                                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                    │
│  │   Traces    │  │   Metrics   │  │    Logs     │                    │
│  │ (Requests,  │  │ (Counters,  │  │(Structured, │                    │
│  │  Handlers)  │  │ Histograms) │  │ Contextual) │                    │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘                    │
│         │                │                │                            │
│         └────────────────┼────────────────┘                            │
│                          │                                             │
│                   ┌──────▼──────┐                                      │
│                   │   OTLP      │                                      │
│                   │  Exporter   │                                      │
│                   └──────┬──────┘                                      │
└──────────────────────────┼──────────────────────────────────────────────┘
                           │
            ┌──────────────┼──────────────┐
            ▼              ▼              ▼
     ┌──────────┐  ┌──────────┐  ┌──────────┐
     │   Seq    │  │  Aspire  │  │ Prometheus│
     │(Logging) │  │Dashboard │  │ /Grafana │
     └──────────┘  └──────────┘  └──────────┘
```

### Custom Metrics

```csharp
// Ordering module metrics
public class OrderingMetrics
{
    private readonly Counter<long> _ordersCreated;
    private readonly Histogram<double> _orderProcessingDuration;

    public OrderingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("NetCommerce.Ordering");

        _ordersCreated = meter.CreateCounter<long>(
            "orders.created",
            description: "Number of orders created");

        _orderProcessingDuration = meter.CreateHistogram<double>(
            "orders.processing.duration",
            unit: "ms",
            description: "Order processing duration");
    }
}
```

---

## Scalability Strategy

### Phase 1: Modular Monolith (Current)

- Single deployment unit
- In-process communication
- Shared PostgreSQL instance (separate schemas)
- Vertical scaling

### Phase 2: Async Messaging

- Replace in-memory bus with RabbitMQ/Azure Service Bus
- Enable horizontal scaling of API instances
- Outbox pattern already supports this transition

### Phase 3: Service Extraction

When a module becomes a scaling bottleneck:

```
BEFORE (Monolith)                    AFTER (Extracted)
┌────────────────────┐              ┌────────────────────┐
│   NetCommerce.Api  │              │   NetCommerce.Api  │
│  ┌──────────────┐  │              │  ┌──────────────┐  │
│  │   Ordering   │  │              │  │   Ordering   │  │
│  ├──────────────┤  │              │  │   (Proxy)    │  │
│  │  Inventory   │──┼─ Extract ──▶ │  └──────────────┘  │
│  │   (Hot)      │  │              └────────────────────┘
│  ├──────────────┤  │                        │
│  │   Payments   │  │                        │ gRPC/HTTP
│  └──────────────┘  │                        ▼
└────────────────────┘              ┌────────────────────┐
                                    │  Inventory Service │
                                    │   (Standalone)     │
                                    └────────────────────┘
```

**Extraction Checklist:**
- [ ] Module has own database schema (✅ already done)
- [ ] Communication via events only (✅ integration events)
- [ ] No shared mutable state (✅ Redis distributed)
- [ ] Independent deployment pipeline

---

## Appendix: Key Files Reference

| Concept | Location |
|---------|----------|
| Aspire Orchestration | `src/NetCommerce.AppHost/Program.cs` |
| Domain Primitives | `src/Kernel/NetCommerce.Kernel.Core/Domain/` |
| Result Pattern | `src/Kernel/NetCommerce.Kernel.Core/Results/` |
| Wolverine Config | `src/Kernel.Adapters/NetCommerce.Kernel.Wolverine/` |
| Order Saga | `src/Ordering/Ordering.Application/Sagas/` |
| API Endpoints | `src/Api/Endpoints/` |
| Architecture Tests | `tests/NetCommerce.Architecture.Tests/` |

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce Architecture Team
