# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# 13 / .NET 10 (Native AOT, `PublishAot=true`)  
**Primary Dependencies**: EF Core 10, Wolverine (outbox), Keycloak 26 (BFF), HybridCache (Redis), Aspire 13.1  
**Storage**: PostgreSQL — per-module schema (`HasDefaultSchema`); Redis for HybridCache + introspection  
**Testing**: xUnit 2.9 + Shouldly + NSubstitute + Bogus (unit); Testcontainers + Respawn (integration); NetArchTest (architecture)  
**Target Platform**: Linux container / .NET Aspire local dev; Native AOT Linux x64 for production  
**Project Type**: Modular monolith web-service (Clean Architecture, DDD, bounded context per module)  
**Performance Goals**: &lt;100 ms p95 reads; &lt;250 ms p95 writes; idempotent retries for all cross-module events  
**Constraints**: AOT-safe serialization only (no reflection); `Result<T>` for business errors (no exceptions); no cross-module DB joins  
**Scale/Scope**: Single deployment unit; 8 bounded contexts; event-driven cross-module coordination via Wolverine outbox

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

[Gates determined based on constitution file]

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# NetCommerce module layout for this feature:
src/{Module}/
├── {Module}.Domain/
│   ├── Aggregates/          # New/modified aggregate roots
│   ├── Events/              # Domain events (IDomainEvent)
│   └── ValueObjects/        # Immutable value objects
├── {Module}.Application/
│   ├── Commands/            # Command + Result<T> handler (static, [WolverineHandler])
│   ├── Queries/             # Query + handler (static)
│   └── Sagas/               # Wolverine saga state machines (if needed)
└── {Module}.Infrastructure/
    ├── Persistence/         # DbContext, entity configs, migrations
    └── Repositories/        # I{Aggregate}Repository implementations

src/Api/Endpoints/{Module}/  # IEndpointGroup Minimal API endpoints
src/Domain.Shared/NetCommerce.Domain.Shared/Events/  # New integration events

tests/NetCommerce.Domain.Tests/{Module}/       # Pure unit tests
tests/NetCommerce.Integration.Tests/{Module}/  # Testcontainers integration tests
```

**Structure Decision**: [Document which modules are touched, new aggregates, new integration events, new API endpoints]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., cross-module query] | [current need] | [why event-driven insufficient] |
| [e.g., new shared event type] | [specific cross-BC contract] | [why existing event type insufficient] |
