# NetCommerce Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-28

## Active Technologies

- **Runtime**: C# 13 / .NET 10 (Native AOT, `PublishAot=true`, `IlcDisableReflection=true`)
- **Orchestration**: .NET Aspire 13.1 (AppHost, ServiceDefaults, resource provisioning)
- **Messaging**: Wolverine with EF Core transactional outbox (static handler classes, `[WolverineHandler]`)
- **ORM**: Entity Framework Core 10 (code-first, per-module `DbContext`, `StronglyTypedIdConvention`)
- **Database**: PostgreSQL — one schema per bounded context
- **Cache**: Redis via `IHybridCache` (HybridCache) + token introspection cache
- **Auth**: Keycloak 26 — OAuth 2.1, PKCE, BFF pattern (`KeycloakTokenProxy`), no ROPC
- **Search**: Meilisearch
- **Logging**: Serilog → Seq (structured, per-request correlation ID)
- **API**: ASP.NET Core Minimal APIs, `IEndpointGroup` convention, API versioning
- **Testing**: xUnit 2.9 · Shouldly 4.3 · NSubstitute 5.3 · Bogus · Testcontainers 4 · Respawn · NetArchTest
- **Build**: `Directory.Build.props` (`TreatWarningsAsErrors=true` in Release), central package management

## Project Structure

```text
NetCommerce/
├── src/
│   ├── Api/                          # Minimal API entrypoint + endpoint groups
│   │   └── Endpoints/{Module}/      # IEndpointGroup per bounded context
│   ├── {Module}/
│   │   ├── {Module}.Application/    # Commands, queries, Wolverine handlers, sagas
│   │   ├── {Module}.Domain/         # Aggregates, entities, value objects, domain events
│   │   └── {Module}.Infrastructure/ # DbContext, repositories, migrations, adapters
│   ├── Kernel/
│   │   ├── NetCommerce.Kernel.Core/        # Entity<TId>, AggregateRoot<TId>, Result<T>, IStronglyTypedId
│   │   ├── NetCommerce.Kernel.Application/ # IPipelineBehavior, ICommand/IQuery interfaces
│   │   ├── NetCommerce.Kernel.AspNetCore/  # Middleware, filters, endpoint conventions
│   │   └── NetCommerce.Kernel.Security/    # KeycloakTokenProxy, auth handlers, rate limiting
│   ├── Kernel.Adapters/
│   │   └── NetCommerce.Kernel.EfCore/      # BaseDbContext, BaseRepository<T,TId>
│   ├── Domain.Shared/
│   │   └── NetCommerce.Domain.Shared/      # Money, integration events (cross-module)
│   ├── NetCommerce.AppHost/             # Aspire host (Postgres, Redis, Keycloak, Seq, Meilisearch)
│   └── NetCommerce.ServiceDefaults/     # OTEL, health checks, resilience defaults
├── tests/
│   ├── NetCommerce.Domain.Tests/        # Pure unit tests (no I/O)
│   ├── NetCommerce.Integration.Tests/   # Testcontainers + Respawn
│   ├── NetCommerce.Architecture.Tests/  # NetArchTest boundary rules
│   ├── NetCommerce.AppHost.Tests/       # Aspire hosting tests
│   └── NetCommerce.LoadTests/           # NBomber load/stress tests
├── specs/                           # spec-kit feature specs (git-tracked)
├── docs/                            # Architecture docs, migration guides
├── .specify/                        # spec-kit configuration and memory
├── Directory.Build.props            # Central build settings
└── Directory.Packages.props         # Central NuGet versions
```

## Commands

```powershell
# Start full system (Postgres, Redis, Keycloak, Seq, Meilisearch)
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj

# Run all tests
dotnet test NetCommerce.slnx -v minimal --nologo

# Run specific test project
dotnet test tests/NetCommerce.Domain.Tests --nologo
dotnet test tests/NetCommerce.Architecture.Tests --nologo
dotnet test tests/NetCommerce.Integration.Tests --nologo

# Release build (zero-warning enforcement)
dotnet build --configuration Release

# Add EF migration for a module
dotnet ef migrations add <Name> `
  --project src/{Module}/{Module}.Infrastructure `
  --startup-project src/NetCommerce.AppHost

# Publish with Native AOT
dotnet publish src/Api/NetCommerce.Api.csproj -c Release
```

## Code Style

**Language**: C# 13 · `#nullable enable` · `<Nullable>enable</Nullable>` in every project  
**Formatting**: 4-space indent · braces on new lines · no trailing whitespace  
**Modern C#**: Primary constructors for services/handlers · `record` for DTOs/value objects · `file` modifier for local test helpers  

### Naming Conventions
- Commands: `VerbNounCommand` / Queries: `GetNounQuery` · `ListNounsQuery`
- Handlers: `VerbNounHandler` (static class with `[WolverineHandler]`)
- Integration events: `NounVerbedIntegrationEvent` · Domain events: `NounVerbedDomainEvent`
- Strongly-typed IDs: `NounId` as `readonly record struct`

### Key Mandatory Patterns

```csharp
// Strongly-typed ID
public readonly record struct ProductId(Guid Value) : IStronglyTypedId<ProductId>
{
    public static ProductId Create(Guid value) => new(value);
    public static ProductId New() => new(Guid.NewGuid());
}

// Result pattern in handlers (NEVER throw for business errors)
public static Result<ProductId> Handle(CreateProductCommand cmd)
    => !isValid
        ? Result.Failure<ProductId>(Error.Validation("Invalid"))
        : Result.Success(product.Id);

// Wolverine handler (static, cascading return)
[WolverineHandler]
public static class OrderPlacedHandler
{
    public static InventoryReserved Handle(OrderPlacedIntegrationEvent @event)
        => new(@event.OrderId, @event.Items);
}
```

### AOT Safety Checklist
- New DTO/response type → add `[JsonSerializable(typeof(MyType))]` to `ApiJsonContext`
- No `Type.GetType(string)`, `Activator.CreateInstance`, or `dynamic`
- No unregistered generic type patterns at runtime

## Recent Changes

- **Phase 7 — Zero-Trust Security** (BFF auth): Added `KeycloakTokenProxy`, BFF endpoints (`/auth/token`, `/auth/refresh`, `/auth/revoke`, `/auth/logout`, `/auth/session`), token introspection middleware, resource-owner+admin elevated authorization, per-user/IP rate limiting. Removed Redis-backed refresh token service (anti-pattern vs Keycloak-native).
- **Phase 6 — Domain Shared Purge**: Migrated all legacy `NetCommerce.SharedKernel` namespaces to `NetCommerce.Domain.Shared`; fully purged obsolete assemblies. See `docs/PHASE_5_SERIALIZATION_MIGRATION.md` for Wolverine table handling.
- **Phase 5 — Modular Messaging**: Full Wolverine outbox integration across all modules; `OrderFulfillmentSaga` with explicit states (`ReservingInventory → InGracePeriod → ProcessingPayment`); strong inventory reservation pattern.

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
