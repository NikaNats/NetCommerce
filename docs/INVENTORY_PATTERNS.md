# Inventory Patterns

Stock reservation lifecycle, concurrency control, and contention handling in the NetCommerce inventory module.

## Reservation Lifecycle

### Overview

NetCommerce uses a **soft reservation model** for inventory management. Stock is not deducted immediately — instead, a time-limited reservation holds the quantity until payment succeeds.

```
Reserve (15 min) → Lock for Payment → Confirm (deduct stock)
                                    ↘ Release (return to pool)
      ↓ (expired)
    Expire (return to pool)
```

### Reservation States

| Status | Value | Description |
|---|---|---|
| `Active` | `0` | Soft reservation with 15-minute TTL |
| `PendingPayment` | `1` | Locked for payment processing (30-minute safety buffer) |
| `Confirmed` | `2` | Payment succeeded — stock deducted from `Quantity` |
| `Released` | `3` | Explicitly released by saga cancellation |
| `Expired` | `4` | TTL elapsed without confirmation |

### State Transitions

```
Active ──────────────────────→ PendingPayment
  │                                 │
  │ (ExpiresAt ≤ now)               │ (Payment succeeds)
  ↓                                 ↓
Expired                         Confirmed
                                    
Active ──────────────────────→ Released
  (Order cancelled)

PendingPayment ──────────────→ Released
  (Payment failed or stuck > 2h)
```

### StockReservation Entity

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Reservation identifier |
| `StockId` | `Guid` | Parent stock aggregate |
| `OrderId` | `Guid` | Associated order |
| `Quantity` | `int` | Reserved quantity |
| `CreatedAt` | `DateTime` | Reservation created |
| `UpdatedAt` | `DateTime` | Last state change |
| `ExpiresAt` | `DateTime` | When reservation expires |
| `Status` | `ReservationStatus` | Current state |
| `ConfirmedAt` | `DateTime?` | When confirmed |
| `ReleasedAt` | `DateTime?` | When released/expired |

### Default Durations

| Duration | Value | Purpose |
|---|---|---|
| `DefaultReservationDuration` | 15 minutes | Active reservation TTL |
| `DefaultPaymentSafetyBuffer` | 30 minutes | PendingPayment extended window |

## Stock Aggregate

### Available Quantity Calculation

Available stock is computed dynamically, excluding active and pending-payment reservations:

```csharp
Available = Quantity - Σ(Active reservations where ExpiresAt > now)
                     - Σ(PendingPayment reservations)
```

Confirmed and expired reservations do not affect availability. Confirmed reservations have already deducted from `Quantity`. Expired reservations have returned to the pool.

### Operations

| Method | Description | Domain Event |
|---|---|---|
| `Reserve(orderId, qty)` | Create soft reservation | `StockReservedDomainEvent`, optionally `LowStockAlertDomainEvent` |
| `LockReservationForPayment(id)` | Promote to PendingPayment | — |
| `ConfirmReservation(id)` | Confirm and deduct stock | `StockDeductedDomainEvent` |
| `ReleaseReservation(id)` | Release back to pool | `StockReleasedDomainEvent` |
| `AddStock(qty)` | Increase total stock | `StockAddedDomainEvent` |
| `RemoveStock(qty, reason)` | Decrease stock (adjustments) | `StockRemovedDomainEvent` |
| `CleanupExpiredReservations()` | Expire stale Active reservations | `ReservationExpiredDomainEvent` |

### Low Stock Alert

When available quantity drops to or below `LowStockThreshold` after a reservation, a `LowStockAlertDomainEvent` is raised.

## Concurrency Control

### Pessimistic Locking

All stock mutations use PostgreSQL's `SELECT ... FOR UPDATE` to acquire row-level locks:

```sql
SELECT s.*, s.xmin 
FROM inventory.stocks AS s 
WHERE s.product_id = @productId 
FOR UPDATE
```

This prevents concurrent transactions from modifying the same stock row simultaneously. The lock is held for the duration of the database transaction.

### Multi-Item Deadlock Prevention

When reserving stock for multiple products (an order with multiple items), products are locked in **deterministic sort order** to prevent deadlocks:

```csharp
var sortedProductIds = command.Items
    .Select(x => x.ProductId)
    .Distinct()
    .OrderBy(id => id)  // Deterministic ordering
    .ToArray();

var stocks = await db.Stocks
    .FromSqlInterpolated(
        $"SELECT s.*, s.xmin FROM inventory.stocks AS s " +
        $"WHERE s.product_id = ANY({sortedProductIds}) " +
        $"ORDER BY s.product_id FOR UPDATE")
    .Include(s => s.Reservations)
    .ToListAsync(ct);
```

### Fail-Closed Verification

After acquiring locks, the handler verifies that **all** requested products were locked. If any are missing (due to missing stock records), the entire reservation fails:

```csharp
if (stocks.Count != sortedProductIds.Length)
{
    return new InventoryReservationFailed(
        command.OrderId,
        "Locking failed: Not all products could be locked",
        UnavailableProductIds: missingIds);
}
```

### Two-Pass Validate-Then-Reserve

Reservation uses a two-pass approach for atomic all-or-nothing semantics:

**Pass 1: Validate** — Check all items have sufficient stock without modifying any entities.

**Pass 2: Reserve** — Only if all items pass validation, create reservations for all items.

This prevents partial reservations where some items are reserved but others fail.

### Optimistic Concurrency

In addition to pessimistic locking, EF Core uses PostgreSQL's `xmin` system column for optimistic concurrency:

```csharp
// xmin is included in SELECT query
// EF Core's concurrency token verification catches stale writes
```

This provides a second layer of protection — if a row was modified between the lock acquisition and the save, a `DbUpdateConcurrencyException` is thrown.

## Message Partitioning

Wolverine routes inventory commands to a dedicated local queue with controlled parallelism:

```csharp
[LocalQueue("inventory-contention")]
```

This limits concurrent inventory operations and reduces database contention under high load.

## Reservation Cleanup Job

### Purpose

The `ReservationCleanupJob` is a `BackgroundService` that periodically releases expired and stuck reservations, returning their quantities to the available pool.

### Configuration

```json
{
  "ReservationCleanup": {
    "IntervalMs": 60000,
    "BatchSize": 100,
    "Enabled": true
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `IntervalMs` | `60000` (1 min) | Cleanup cycle interval |
| `BatchSize` | `100` | Max reservations per batch |
| `Enabled` | `true` | Enable/disable the job |

### Cleanup Targets

The job handles two categories of leaked reservations:

#### 1. Expired Active Reservations

```sql
WHERE status = 'Active' AND expires_at <= NOW()
```

These are reservations where the 15-minute checkout window expired without progressing to payment.

#### 2. Stuck PendingPayment Reservations

```sql
WHERE status = 'PendingPayment' AND updated_at <= NOW() - INTERVAL '2 hours'
```

These are reservations locked for payment but where no confirmation or release arrived within 2 hours. This catches scenarios where the payment webhook was lost or the saga timed out without releasing.

### Cleanup Process

1. Query expired `Active` reservations (limited by `BatchSize`)
2. Query stuck `PendingPayment` reservations (updated > 2 hours ago)
3. Load parent `Stock` aggregates with reservations
4. Call `stock.ReleaseReservation(id)` for each leaked reservation
5. Save changes in a single transaction
6. Log the count of released reservations

### Testability

The job uses `TimeProvider` for deterministic time operations:

```csharp
public ReservationCleanupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ReservationCleanupJob> logger,
    IOptions<ReservationCleanupOptions> options,
    TimeProvider? timeProvider = null)
```

Tests inject a `FakeTimeProvider` to control time advancement without real delays.

## Saga Integration

### Order Fulfillment Flow

The `OrderFulfillmentSaga` coordinates inventory operations:

```
OrderCreated
  → ReserveInventoryCommand (saga → inventory handler)
    → InventoryReserved / InventoryReservationFailed (response → saga)

InventoryReserved
  → LockInventoryCommand (saga → inventory handler)
    → InventoryLocked / InventoryLockFailed (response → saga)

PaymentSucceeded
  → ConfirmInventoryCommand (saga → inventory handler)
    → InventoryConfirmed (response → saga)

OrderCancelled / PaymentFailed
  → ReleaseInventoryCommand (saga → inventory handler)
    → InventoryReleased (response → saga)
```

### Compensation

If payment fails after inventory is reserved, the saga publishes `ReleaseInventoryCommand` to return the reserved quantities to the available pool.

## Domain Events

| Event | Trigger | Description |
|---|---|---|
| `StockReservedDomainEvent` | `Reserve()` | Reservation created |
| `LowStockAlertDomainEvent` | `Reserve()` if below threshold | Available stock critically low |
| `StockDeductedDomainEvent` | `ConfirmReservation()` | Actual stock reduced |
| `StockReleasedDomainEvent` | `ReleaseReservation()` | Reservation released |
| `StockAddedDomainEvent` | `AddStock()` | Inventory received |
| `StockRemovedDomainEvent` | `RemoveStock()` | Manual adjustment |
| `ReservationExpiredDomainEvent` | `CleanupExpiredReservations()` | TTL elapsed |

## Monitoring

### Key Metrics

| Metric | Description | Alert If |
|---|---|---|
| Active reservations | Count of `Active` status reservations | Steadily growing |
| Stuck PendingPayment | Reservations stuck > 2 hours | > 0 |
| Cleanup releases | Reservations released per cleanup cycle | Consistently high |
| Low stock alerts | Products below threshold | Based on business rules |
| Failed reservations | `InventoryReservationFailed` events | > 0 |

### Seq Log Queries

```
# Expired reservations released
@Message like '%Released expired reservation%'

# Stuck payment reservations
@Message like '%Released stuck pending-payment%'

# Reservation failures (insufficient stock)
@Message like '%Insufficient stock%'

# Low stock alerts
SourceContext like '%Stock%' and @Message like '%Low stock%'

# Deadlock or contention
@Message like '%FOR UPDATE%' and @Level >= 'Warning'
```

## Related Documentation

- [Domain Model](DOMAIN_MODEL.md) — Stock and StockReservation entities
- [Messaging Patterns](MESSAGING_PATTERNS.md) — saga inventory commands
- [Operations](OPERATIONS.md) — reservation monitoring
- [Architecture](ARCHITECTURE.md) — inventory module boundaries
