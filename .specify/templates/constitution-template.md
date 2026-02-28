# NetCommerce Constitution

## Core Principles

### I. Modular Monolith — Hard Boundary Enforcement
Each bounded context (Catalog, Ordering, Inventory, Payments, Shipping, Media, Basket, Finance) is a fully isolated module with its own:
- Database schema — no cross-module FK relationships in the database
- `DbContext` inheriting `BaseDbContext` with `HasDefaultSchema("<module>")`
- Application / Domain / Infrastructure project split per Clean Architecture
- Communication only via Wolverine integration events (transactional outbox) — no direct project-to-project service calls across modules

Cross-module dependencies are forbidden. Shared concepts live exclusively in `NetCommerce.Domain.Shared` (value objects, integration events) or `NetCommerce.Kernel.*` (infrastructure primitives).

### II. Domain-Driven Design — Aggregates Own Their Invariants
- Every write operation flows through an Aggregate Root; no direct repository updates to child entities
- Domain events raised via `RaiseDomainEvent()` on the aggregate; never constructed outside
- Integration events live in `src/Domain.Shared/NetCommerce.Domain.Shared/Events/` and are published via Wolverine outbox
- Value objects inherit `ValueObject`, override `GetEqualityComponents()`, and are immutable
- Strongly-typed IDs are mandatory: `readonly record struct OrderId(Guid Value) : IStronglyTypedId<OrderId>`; never use raw `Guid` as an identifier

### III. Result Pattern — No Exceptions for Business Logic (NON-NEGOTIABLE)
- All command handlers return `Result<T>` from `NetCommerce.Kernel.Core.Results`
- Business errors use `Error.Validation(...)`, `Error.NotFound(...)`, `Error.Conflict(...)` — never `throw`
- Exceptions are reserved for infrastructure failures only
- API layer maps `Result<T>` to HTTP status codes; domain layer never references HTTP types

### IV. Test-First for Domain and Security Logic
- Domain logic and security handlers: write failing tests first, then implement (TDD)
- Unit tests (`NetCommerce.Domain.Tests`): xUnit + Shouldly + NSubstitute + Bogus — no I/O, pure in-memory
- Integration tests (`NetCommerce.Integration.Tests`): Testcontainers + real Postgres/Redis + Respawn between tests
- Architecture tests (`NetCommerce.Architecture.Tests`): NetArchTest validates Clean Architecture boundaries on every PR
- `TreatWarningsAsErrors=true` in Release/CI — zero warnings policy

### V. Wolverine Messaging — Outbox by Default
- All cross-module communication via Wolverine handlers with `[WolverineHandler]` attribute
- Handlers are static classes; cascading messages are returned as values (not published imperatively)
- Integration events are always persisted in the same transaction as domain changes (transactional outbox)
- Sagas use explicit state machines with named states; no ambient state
- Idempotency is the handler author's responsibility — use `X-Idempotency-Key` for critical endpoints

### VI. Native AOT Compatibility (NON-NEGOTIABLE)
- `PublishAot=true`, `IlcDisableReflection=true` — no runtime reflection anywhere in production code
- All JSON serialization via source-generated `JsonSerializerContext` (`[JsonSerializable(typeof(...))]`)
- New types added to `ApiJsonContext` (API layer) or module-specific context as appropriate
- No `dynamic`, no `System.Reflection.Emit`, no unbound generics at runtime
- Verify AOT compatibility before merging features that add new serialized types

### VII. Security — Zero-Trust, Keycloak-Native
- API never issues tokens; all OAuth 2.0 token lifecycle delegated to Keycloak (BFF pattern)
- ROPC (password grant) is explicitly rejected — Authorization Code + PKCE only for users
- Client Credentials for M2M; Token Exchange (RFC 8693) for downstream service calls
- Authorization uses role+resource model: `[RequireRole]` + `ResourceOwnerAuthorization` + `AdminElevatedAuthorization`
- Per-user and per-IP rate limiting applied to all auth and sensitive endpoints

## Technology Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10, Native AOT |
| Orchestration | .NET Aspire 13.1 |
| Messaging | Wolverine (transactional outbox) |
| ORM | EF Core 10 (per-module DbContext, code-first migrations) |
| Database | PostgreSQL (per module schema) |
| Cache | Redis (HybridCache, introspection cache) |
| Auth | Keycloak 26 (OIDC, OAuth 2.1, PKCE) |
| Search | Meilisearch |
| Logging | Serilog → Seq |
| Testing | xUnit 2.9 · Shouldly 4.3 · NSubstitute 5.3 · Bogus · Testcontainers · NetArchTest |
| CI | GitHub Actions — `dotnet test NetCommerce.slnx -v minimal --nologo` |

## Module Structure Pattern

Every bounded context follows this layout:

```
src/{Module}/
├── {Module}.Application/   # Commands, queries, Wolverine handlers, sagas
├── {Module}.Domain/        # Aggregates, entities, value objects, domain events
└── {Module}.Infrastructure/# EF Core DbContext, repositories, external adapters
```

Shared kernel assemblies:
- `NetCommerce.Kernel.Core` — `Entity<TId>`, `AggregateRoot<TId>`, `Result<T>`, `IStronglyTypedId`
- `NetCommerce.Kernel.EfCore` — `BaseDbContext`, `BaseRepository<TAggregate, TId>`
- `NetCommerce.Kernel.Security` — `KeycloakTokenProxy`, auth handlers, rate limiting
- `NetCommerce.Domain.Shared` — `Money` (GEL default), integration events

## Development Workflow

1. **Feature branch**: `git checkout -b <NNN>-<feature-name>` from `main`
2. **Spec**: Use `/speckit.specify` to define requirements; get approval before planning
3. **Plan**: Use `/speckit.plan` with tech context (module, aggregates, events, endpoints)
4. **Tasks**: Use `/speckit.tasks` to generate ordered, dependency-resolved task list
5. **Implement**: Domain logic first → application handlers → infrastructure → API endpoints
6. **Tests**: Unit tests alongside implementation; integration tests for full slice
7. **Quality gates** before PR:
   - `dotnet test NetCommerce.slnx -v minimal --nologo` → 0 failures
   - Architecture test pass
   - `dotnet build --configuration Release` → 0 errors, 0 warnings
8. **PR**: Link spec.md; describe module boundary impact; call out new integration events

## Governance

- This constitution supersedes all per-feature decisions; conflicts must be raised as a constitution amendment
- AOT and Result Pattern rules are non-negotiable — no exceptions without explicit constitution amendment
- New modules require: new DbContext, new schema, Wolverine handler registration, migration in AppHost
- Serialization risk: Wolverine saga state uses fully qualified type names — clear tables before deploying type renames; see `docs/PHASE_5_SERIALIZATION_MIGRATION.md`
- `Money` value object uses GEL as default currency; multi-currency must be explicit
- Architecture tests run on every PR — they are the automated constitution enforcement layer

**Version**: 1.0.0 | **Ratified**: 2026-02-28 | **Last Amended**: 2026-02-28
