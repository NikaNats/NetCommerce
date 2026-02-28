# Architecture Diagrams

Visual representations of NetCommerce's modular monolith architecture, data flows, and deployment topology.

## System Context

```
                            ┌─────────────┐
                            │   Browser   │
                            │   Client    │
                            └──────┬──────┘
                                   │ HTTPS
                            ┌──────▼──────┐
                            │  Keycloak   │
                            │   (OIDC)    │
                            └──────┬──────┘
                                   │ JWT
                            ┌──────▼──────┐
                            │ NetCommerce │
                            │    API      │
                            └──┬──┬──┬──┬─┘
                ┌──────────────┘  │  │  └──────────────┐
                │                 │  │                  │
        ┌───────▼───────┐ ┌──────▼──▼──────┐  ┌───────▼───────┐
        │  PostgreSQL   │ │     Redis      │  │    Stripe     │
        │   (6 schemas) │ │  (cache+basket)│  │  (payments)   │
        └───────────────┘ └────────────────┘  └───────────────┘
                │
        ┌───────▼───────┐ ┌────────────────┐  ┌───────────────┐
        │  MeiliSearch  │ │  Azure Blob    │  │     Seq       │
        │   (search)    │ │  (media)       │  │   (logs)      │
        └───────────────┘ └────────────────┘  └───────────────┘
```

## Module Dependency Graph

```
                    ┌─────────────────────────┐
                    │       API Layer          │
                    │  (Minimal APIs, Auth,    │
                    │   Middleware, JSON Gen)   │
                    └────┬───┬───┬───┬───┬────┘
                         │   │   │   │   │
          ┌──────────────┤   │   │   │   ├──────────────┐
          │              │   │   │   │   │              │
    ┌─────▼────┐  ┌──────▼───▼───▼───▼───▼──────┐ ┌────▼─────┐
    │ Catalog  │  │  Ordering  Inventory         │ │ Finance  │
    │ .App     │  │  .App      .App     Payments │ │ .App     │
    │ .Domain  │  │  .Domain   .Domain  .App     │ │ .Domain  │
    │ .Infra   │  │  .Infra    .Infra   .Domain  │ │ .Infra   │
    └─────┬────┘  │                     .Infra   │ └────┬─────┘
          │       └──────────┬───────────────────┘      │
          │                  │                          │
    ┌─────▼──────────────────▼──────────────────────────▼─────┐
    │                    Domain.Shared                         │
    │     Integration Events · Saga Messages · Money VO       │
    ├─────────────────────────────────────────────────────────┤
    │                    Kernel.Core                           │
    │     Entity · AggregateRoot · ValueObject · Result       │
    ├─────────────────────────────────────────────────────────┤
    │                  Kernel.Adapters                         │
    │    EfCore · AspNetCore · Compliance · SourceGenerators   │
    └─────────────────────────────────────────────────────────┘
```

## Clean Architecture Layer Diagram

```
    ┌───────────────────────────────────────────────┐
    │              Infrastructure Layer              │
    │                                               │
    │  ┌──────────────────────────────────────────┐ │
    │  │           Application Layer              │ │
    │  │                                          │ │
    │  │  ┌────────────────────────────────────┐  │ │
    │  │  │          Domain Layer              │  │ │
    │  │  │                                    │  │ │
    │  │  │  ┌──────────────────────────────┐  │  │ │
    │  │  │  │       Kernel.Core           │  │  │ │
    │  │  │  │                              │  │  │ │
    │  │  │  │  Entity<TId>                │  │  │ │
    │  │  │  │  AggregateRoot<TId>         │  │  │ │
    │  │  │  │  ValueObject                │  │  │ │
    │  │  │  │  Result<T> / Error          │  │  │ │
    │  │  │  │  IStronglyTypedId<T>        │  │  │ │
    │  │  │  │  Guard                      │  │  │ │
    │  │  │  └──────────────────────────────┘  │  │ │
    │  │  │                                    │  │ │
    │  │  │  Aggregates, Entities              │  │ │
    │  │  │  Value Objects, Domain Events      │  │ │
    │  │  │  Repository Interfaces             │  │ │
    │  │  └────────────────────────────────────┘  │ │
    │  │                                          │ │
    │  │  Commands, Queries, Handlers             │ │
    │  │  Service Interfaces                      │ │
    │  │  Saga State Machines                     │ │
    │  └──────────────────────────────────────────┘ │
    │                                               │
    │  EF Core DbContexts, Repositories            │
    │  External API Clients (Stripe, MeiliSearch)   │
    │  Background Services                          │
    └───────────────────────────────────────────────┘
```

**Dependency Rule:** Dependencies always point inward. Domain never references Application or Infrastructure.

## Order Fulfillment Saga State Machine

```
                        ┌──────────────┐
                        │  NotStarted  │
                        │     (0)      │
                        └──────┬───────┘
                               │ StartOrderFulfillmentCommand
                        ┌──────▼───────┐
                 ┌──────│  Reserving   │──────┐
                 │      │  Inventory   │      │
                 │      │     (1)      │      │
                 │      └──────┬───────┘      │
                 │             │               │ InventoryReservationFailed
                 │             │ Inventory     │ or Timeout (5min)
                 │             │ Reserved      │
                 │      ┌──────▼───────┐      │
                 │      │  InGrace     │      │
                 │      │  Period (2)  │      │      ┌──────────────┐
                 │      └──────┬───────┘      ├─────>│    Failed    │
                 │             │               │      │     (8)      │
                 │             │ GracePeriod   │      └──────────────┘
                 │             │ Timeout       │
                 │      ┌──────▼───────┐      │
                 │      │  Locking     │      │
                 │      │  Inventory   │──────┘
                 │      │     (3)      │
                 │      └──────┬───────┘
                 │             │ InventoryLocked
                 │      ┌──────▼───────┐
                 │      │ Processing   │
                 │      │  Payment (4) │──────┐
                 │      └──────┬───────┘      │ PaymentFailed
                 │             │               │ or Timeout (30min)
                 │             │ Payment       │
                 │             │ Succeeded     │
                 │      ┌──────▼───────┐      │
                 │      │ Confirming   │      │
                 │      │ Inventory(5) │──────┤
                 │      └──────┬───────┘      │ InventoryConfirmationFailed
                 │             │               │
                 │             │ Inventory     │
                 │             │ Confirmed     │
                 │      ┌──────▼───────┐      │
                 │      │  Completed   │      │
                 │      │     (7)      │      │
                 │      └──────────────┘      │
                 │                            │
                 │                     ┌──────▼───────┐
                 │                     │ Compensating │
                 │                     │     (6)      │
                 │                     └──────┬───────┘
                 │                            │
                 │              ┌─────────────┼─────────────┐
                 │              │             │             │
                 │       RefundCompleted  RefundFailed  Timeout (4h)
                 │              │             │             │
                 │       ┌──────▼──────┐ ┌────▼───────┐    │
                 │       │   Failed    │ │  Manual    │◄───┘
                 └──────>│    (8)      │ │Intervention│
                         └─────────────┘ │    (9)     │
                                         └────────────┘
```

### Saga Timeout Durations

| Timeout | Duration | Trigger Condition |
|---|---|---|
| Inventory Reservation | 5 minutes | Stuck in `ReservingInventory` |
| Grace Period | 5 minutes | Customer cancellation window |
| Payment | 30 minutes | Awaiting Stripe webhook |
| Inventory Confirmation | 5 minutes | Stuck in `ConfirmingInventory` |
| Compensation Stalled | 4 hours | Refund not completing |

## Data Flow: Order Placement

```
Client                API              Ordering           Inventory          Payments           Finance
  │                    │                  │                   │                  │                 │
  │ POST /orders       │                  │                   │                  │                 │
  │ X-Idempotency-Key  │                  │                   │                  │                 │
  │───────────────────>│                  │                   │                  │                 │
  │                    │ CreateOrder      │                   │                  │                 │
  │                    │ Command          │                   │                  │                 │
  │                    │─────────────────>│                   │                  │                 │
  │                    │                  │ StartOrderFulfill │                  │                 │
  │                    │                  │ mentCommand       │                  │                 │
  │                    │                  │──────────────────>│                  │                 │
  │                    │                  │                   │                  │                 │
  │                    │                  │ ReserveInventory  │                  │                 │
  │                    │                  │ Command           │                  │                 │
  │                    │                  │──────────────────>│                  │                 │
  │                    │                  │                   │ SELECT FOR UPDATE│                 │
  │                    │                  │                   │ (pessimistic)    │                 │
  │                    │                  │  InventoryReserved│                  │                 │
  │                    │                  │<──────────────────│                  │                 │
  │                    │                  │                   │                  │                 │
  │                    │                  │    [5min Grace Period]               │                 │
  │                    │                  │                   │                  │                 │
  │                    │                  │ RequestPayment    │                  │                 │
  │                    │                  │ Command           │                  │                 │
  │                    │                  │─────────────────────────────────────>│                 │
  │                    │                  │                   │                  │ Stripe API      │
  │                    │                  │                   │                  │────────────>    │
  │                    │                  │                   │                  │                 │
  │                    │ Stripe Webhook   │                   │                  │                 │
  │                    │<══════════════════════════════════════════════════════──│                 │
  │                    │                  │  PaymentSucceeded │                  │                 │
  │                    │                  │<─────────────────────────────────────│                 │
  │                    │                  │                   │                  │  Audit Entry    │
  │                    │                  │                   │                  │────────────────>│
  │                    │                  │ ConfirmInventory  │                  │                 │
  │                    │                  │──────────────────>│                  │                 │
  │                    │                  │                   │ Deduct stock     │                 │
  │                    │                  │ InventoryConfirmed│                  │                 │
  │                    │                  │<──────────────────│                  │                 │
  │                    │                  │                   │                  │                 │
  │  SignalR: Status   │                  │                   │                  │                 │
  │<═══════════════════│  OrderCompleted  │                   │                  │                 │
  │                    │                  │                   │                  │                 │
```

## Stripe Webhook Processing

```
Stripe                     API                        Payments              Ordering Saga
  │                         │                            │                      │
  │ POST /api/webhooks/     │                            │                      │
  │ stripe                  │                            │                      │
  │ Stripe-Signature: ...   │                            │                      │
  │────────────────────────>│                            │                      │
  │                         │ Verify Signature           │                      │
  │                         │ (EventUtility              │                      │
  │                         │  .ConstructEvent)          │                      │
  │                         │                            │                      │
  │                         │ TryClaimEvent              │                      │
  │                         │ (INSERT ON CONFLICT        │                      │
  │                         │  DO NOTHING)               │                      │
  │                         │───────────────────────────>│                      │
  │                         │                            │                      │
  │                         │ ProcessExternalPayment     │                      │
  │                         │ Confirmation               │                      │
  │                         │───────────────────────────>│                      │
  │                         │                            │ Update Transaction   │
  │                         │                            │ Status               │
  │                         │                            │                      │
  │                         │                            │ PaymentSucceeded     │
  │                         │                            │─────────────────────>│
  │                         │                            │                      │ Continue Saga
  │  200 OK                 │                            │                      │
  │<────────────────────────│                            │                      │
```

## Aspire Resource Topology

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Aspire AppHost                               │
│                                                                      │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────────────┐   │
│  │  PostgreSQL  │    │    Redis     │    │      Keycloak        │   │
│  │  (persistent)│    │  (persistent)│    │    (persistent)      │   │
│  │              │    │              │    │  realm: netcommerce   │   │
│  │  ┌─CatalogDb│    │  RedisInsight│    │  token-exchange      │   │
│  │  ├─OrderingDb│    └──────────────┘    │  fine-grained-authz  │   │
│  │  ├─Inventory │                        └──────────────────────┘   │
│  │  ├─PaymentsDb│    ┌──────────────┐    ┌──────────────────────┐   │
│  │  ├─ShippingDb│    │ MeiliSearch  │    │     Azurite          │   │
│  │  └─KeycloakDb│    │  (persistent)│    │  Blob:10000          │   │
│  │              │    │              │    │  Queue:10001         │   │
│  │  PgAdmin:5050│    └──────────────┘    │  Table:10002         │   │
│  └──────────────┘                        └──────────────────────┘   │
│                       ┌──────────────┐                               │
│                       │     Seq      │                               │
│                       │  (persistent)│                               │
│                       │  structured  │                               │
│                       │    logs      │                               │
│                       └──────────────┘                               │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │                    NetCommerce.Api                            │    │
│  │  References: CatalogDb, OrderingDb, InventoryDb, PaymentsDb │    │
│  │              redis, blobs, seq, meilisearch, keycloak       │    │
│  │  Health: /health/ready                                       │    │
│  └──────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
```

## Database Schema Isolation

```
PostgreSQL Instance
├── catalog schema
│   ├── Products
│   ├── Categories
│   └── ProductImages
├── ordering schema
│   ├── Orders
│   ├── OrderItems
│   ├── wolverine_incoming_envelopes
│   ├── wolverine_outgoing_envelopes
│   └── wolverine_saga_state (OrderFulfillmentSaga)
├── inventory schema
│   ├── Stocks
│   └── StockReservations
├── payments schema
│   ├── PaymentTransactions
│   └── ProcessedWebhookEvents
├── shipping schema
│   ├── Shipments
│   └── ShipmentItems
└── finance schema
    ├── FinancialAuditEntries
    ├── ReconciliationSessions
    └── ReconciliationDiscrepancies
```

No cross-schema foreign keys. Cross-module data access occurs only through Wolverine messaging.

## Reconciliation Engine Flow

```
                    ┌───────────────────┐
                    │  T+1 Daily Trigger │
                    │ (CheckDailyRecon   │
                    │  ciliation)        │
                    └────────┬──────────┘
                             │
                    ┌────────▼──────────┐
                    │ Fetch Internal     │
                    │ Completed Txns     │
                    │ (PaymentsDb)       │
                    └────────┬──────────┘
                             │
                    ┌────────▼──────────┐
                    │ Fetch External     │
                    │ PSP Ledger         │
                    │ (Stripe API)       │
                    └────────┬──────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
     ┌────────▼────────┐     │    ┌─────────▼────────┐
     │ Left Outer Join │     │    │ Right Outer Join  │
     │ Internal→External│    │    │ External→Internal │
     │                  │     │    │                   │
     │ Finds:           │     │    │ Finds:            │
     │ - MissingExternal│     │    │ - GHOST CHARGES   │
     │ - AmountMismatch │     │    │ - MissingInternal │
     └────────┬────────┘     │    └─────────┬────────┘
              │              │              │
              └──────────────┼──────────────┘
                             │
                    ┌────────▼──────────┐
                    │ Publish Alerts     │
                    │                   │
                    │ Ghost Charges     │
                    │ → CriticalFinan   │
                    │   cialAlert       │
                    │                   │
                    │ Amount Mismatches │
                    │ > $100 threshold  │
                    │ → Alert           │
                    └───────────────────┘
```

## Native AOT Compilation Pipeline

```
Source Code
    │
    ▼
┌──────────────────┐
│ dotnet publish    │
│ -c Release        │
│ -r linux-x64      │
│ -p:PublishAot=true │
└────────┬─────────┘
         │
    ┌────▼─────────────┐
    │ Roslyn Compilation│
    │ + Source Generators│
    │                   │
    │ ApiJsonContext    │
    │ ConfigBinding Gen │
    │ Wolverine Codegen │
    └────────┬─────────┘
             │
    ┌────────▼─────────┐
    │ IL Linker (Trim)  │
    │                   │
    │ Removes unused    │
    │ code paths        │
    └────────┬─────────┘
             │
    ┌────────▼─────────┐
    │ ILC (AOT Compiler)│
    │                   │
    │ Native binary     │
    │ ~45MB             │
    │ No JIT needed     │
    └────────┬─────────┘
             │
    ┌────────▼─────────┐
    │ Docker (Chiseled) │
    │                   │
    │ No shell          │
    │ Non-root (1654)   │
    │ ~65MB total       │
    │ ~80ms startup     │
    └──────────────────┘
```

## Related Documentation

- [Architecture](ARCHITECTURE.md) — design principles and module structure
- [Messaging Patterns](MESSAGING_PATTERNS.md) — saga state machine details
- [Native AOT](NATIVE_AOT_VERIFICATION.md) — verification checkpoints
- [Deployment](DEPLOYMENT.md) — production deployment topology
