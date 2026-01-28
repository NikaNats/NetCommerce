# NetCommerce - AI Coding Instructions

## Architecture Overview

NetCommerce is a **Modular Monolith** built with .NET 10 and Aspire 13.1, implementing DDD principles with Clean Architecture. Each bounded context (Catalog, Ordering, Inventory, Payments, Shipping, Media, Basket, Finance) has its own database and communicates via **Wolverine** for messaging.

### Module Structure Pattern
```
src/{Module}/
├── {Module}.Application/     # Commands, queries, handlers (Wolverine)
├── {Module}.Domain/          # Aggregates, entities, value objects
├── {Module}.Infrastructure/  # EF Core, external services
```

Key shared assemblies:
- `src/Kernel/NetCommerce.Kernel.Core` - Domain primitives (`Entity<TId>`, `AggregateRoot<TId>`, `Result<T>`, `IStronglyTypedId`)
- `src/Kernel.Adapters/NetCommerce.Kernel.EfCore` - `BaseDbContext`, `BaseRepository<TAggregate, TId>`
- `src/Domain.Shared/NetCommerce.Domain.Shared` - Cross-cutting value objects (`Money`), integration events

## Critical Conventions

### Strongly Typed IDs
All entity IDs must implement `IStronglyTypedId<T>` as a `readonly record struct`:
```csharp
public readonly record struct OrderId(Guid Value) : IStronglyTypedId<OrderId>
{
    public static OrderId Create(Guid value) => new(value);
    // IParsable<T> implementation required
}
```
EF Core converters auto-register via `StronglyTypedIdConvention` in `BaseDbContext`.

### Result Pattern (No Exceptions for Business Errors)
All command handlers return `Result<T>` from `NetCommerce.Kernel.Core.Results`:
```csharp
public static Result<Guid> Handle(CreateOrderCommand command)
    => stockAvailable ? Result.Success(orderId) : Result.Failure<Guid>(Error.Validation("Out of stock"));
```

### Wolverine Message Handlers
Use static handler classes with `[WolverineHandler]` attribute. Wolverine uses cascading messages:
```csharp
[WolverineHandler]
public static class OrderSubmittedHandler
{
    // Return value becomes cascading message (published via outbox)
    public static InventoryReserved Handle(OrderSubmittedIntegrationEvent @event, ILogger logger)
        => new InventoryReserved(@event.OrderId, ...);
}
```

### Domain Events & Integration Events
- **Domain events** (`IDomainEvent`): Internal to a module, raised via `RaiseDomainEvent()` on aggregates
- **Integration events** (`IntegrationEvent`): Cross-module, published via Wolverine transactional outbox

Integration events live in `src/Domain.Shared/NetCommerce.Domain.Shared/Events/`.

### EF Core DbContext Pattern
Each module has its own `DbContext` inheriting `BaseDbContext`, with isolated schema:
```csharp
public class OrderingDbContext : BaseDbContext
{
    public const string Schema = "ordering";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
    }
}
```

### Value Objects
Inherit from `ValueObject` and override `GetEqualityComponents()`. The `Money` value object uses **GEL** as default currency:
```csharp
Money.Create(100m);           // 100 GEL
Money.Create(50m, "USD");     // 50 USD
```

## Build & Test Commands

```powershell
# Run entire solution with Aspire (starts Postgres, Redis, Keycloak, Seq, Meilisearch)
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj

# Run all tests
dotnet test NetCommerce.slnx -v minimal --nologo

# Architecture tests only (validates Clean Architecture boundaries)
dotnet test tests/NetCommerce.Architecture.Tests --nologo
```

The `Directory.Build.props` enforces **TreatWarningsAsErrors** in Release/CI builds.

## Key Patterns to Follow

1. **Transactional Outbox**: Integration events are saved in the same transaction as domain changes via Wolverine's EF Core integration
2. **Saga State Machines**: Complex workflows use `OrderFulfillmentSaga` with explicit states (`ReservingInventory` → `InGracePeriod` → `ProcessingPayment`)
3. **Price Snapshotting**: Order items capture product title/price at order time
4. **Idempotency**: Critical endpoints require `X-Idempotency-Key` header; use `IdempotencyFilter`
5. **Soft Reservations**: Inventory is reserved (not deducted) during checkout; confirmed on payment success

## Test Categories

| Test Project | Purpose |
|-------------|---------|
| `NetCommerce.Architecture.Tests` | Clean Architecture boundary validation via NetArchTest |
| `NetCommerce.Domain.Tests` | Unit tests for domain logic |
| `NetCommerce.Integration.Tests` | Testcontainers-based tests with real Postgres/Redis |
| `NetCommerce.LoadTests` | NBomber load tests for high-concurrency scenarios |
| `NetCommerce.AppHost.Tests` | Aspire hosting configuration tests |

Integration tests use `IntegrationTestFixture` with Respawn for database cleanup between tests.

## API Endpoints

Endpoints are organized in `src/Api/Endpoints/{Module}/` using Minimal APIs with API versioning. Follow the `IEndpointGroup` pattern and map to `ApiVersionSet`.
