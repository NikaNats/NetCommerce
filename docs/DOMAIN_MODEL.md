# Domain Model

Complete reference for all aggregates, entities, value objects, and domain events across NetCommerce's bounded contexts.

## Catalog Context

### Product (Aggregate Root)

The central aggregate for the product catalog. Manages product lifecycle from draft to published/archived.

| Property | Type | Description |
|---|---|---|
| `Id` | `ProductId` | Strongly typed ID |
| `Title` | `string` | Product display name |
| `Description` | `string` | Product description |
| `Slug` | `string` | URL-friendly identifier (auto-generated) |
| `Sku` | `string` | Stock Keeping Unit |
| `Price` | `Money` | Current price (amount + currency) |
| `Status` | `ProductStatus` | `Draft`, `Published`, `Archived` |
| `CategoryId` | `CategoryId?` | Optional category assignment |
| `Images` | `List<ProductImage>` | Ordered product images |
| `TenantId` | `string` | Multi-tenant isolation |
| `CreatedAt` | `DateTime` | Creation timestamp |
| `UpdatedAt` | `DateTime?` | Last modification timestamp |

**Behavior:**
- `Create(title, price, sku, ...)` — factory method, raises `ProductCreatedEvent`
- `Publish()` — transitions Draft → Published, raises `ProductPublishedEvent`
- `Archive()` — soft delete, transitions to Archived
- `UpdatePrice(newPrice)` — price change with domain event
- `AddImage(imageKey, displayOrder, isPrimary)` — image management

**Domain Events:**
- `ProductCreatedEvent` — triggers MeiliSearch index sync
- `ProductPublishedEvent` — triggers cache invalidation
- `ProductPriceChangedEvent` — triggers dependent updates

**Invariants:**
- Title must not be empty
- Price must be positive
- SKU must be unique within tenant
- Only Draft products can be published
- Published products can be archived but not reverted to Draft

### Category (Aggregate Root)

Hierarchical product categorization with parent-child relationships.

| Property | Type | Description |
|---|---|---|
| `Id` | `CategoryId` | Strongly typed ID |
| `Name` | `string` | Category display name |
| `Slug` | `string` | URL-friendly identifier |
| `Description` | `string?` | Optional description |
| `ParentCategoryId` | `CategoryId?` | Parent for hierarchical trees |
| `DisplayOrder` | `int` | Sorting position |

**Behavior:**
- `Create(name, parentId?, displayOrder)` — factory method
- `Update(name, description, displayOrder)` — modification
- Children retrieved via query (not embedded in aggregate)

### ProductImage (Entity)

Owned entity within the Product aggregate.

| Property | Type | Description |
|---|---|---|
| `Id` | `ProductImageId` | Strongly typed ID |
| `ImageKey` | `string` | Azure Blob storage key |
| `DisplayOrder` | `int` | Image ordering |
| `IsPrimary` | `bool` | Primary display image flag |

## Basket Context

### ShoppingBasket (Aggregate Root)

Redis-backed shopping basket with per-user isolation. Not persisted in PostgreSQL.

| Property | Type | Description |
|---|---|---|
| `CustomerId` | `string` | User identifier (from JWT claims) |
| `Items` | `List<BasketItem>` | Basket line items |
| `CreatedAt` | `DateTime` | Basket creation time |
| `UpdatedAt` | `DateTime?` | Last modification time |

### BasketItem (Entity)

| Property | Type | Description |
|---|---|---|
| `ProductId` | `Guid` | Referenced product |
| `ProductName` | `string` | Snapshot at add time |
| `Sku` | `string?` | Product SKU |
| `Quantity` | `int` | Item count |
| `UnitPrice` | `decimal` | Price snapshot at add time |
| `ImageUrl` | `string?` | Product image URL |

**Design Decision:** Basket items capture product title and price at the time of addition (price snapshotting). The order pipeline re-validates prices during the grace period.

## Ordering Context

### Order (Aggregate Root)

The primary order aggregate. Created atomically from basket contents.

| Property | Type | Description |
|---|---|---|
| `Id` | `OrderId` | Strongly typed ID |
| `OrderNumber` | `string` | Human-readable order number |
| `CustomerId` | `Guid` | Customer who placed the order |
| `Status` | `OrderStatus` | Current order lifecycle state |
| `Items` | `List<OrderItem>` | Ordered line items |
| `TotalAmount` | `Money` | Computed total |
| `ShippingAddress` | `Address` | Delivery address |
| `IdempotencyKey` | `Guid` | Duplicate submission prevention |
| `CreatedAt` | `DateTime` | Order creation time |
| `CompletedAt` | `DateTime?` | Completion timestamp |
| `CancelledAt` | `DateTime?` | Cancellation timestamp |
| `CancellationReason` | `string?` | Reason if cancelled |

**Behavior:**
- `Create(customerId, items, address, idempotencyKey)` — factory, raises `OrderCreatedEvent`
- `Cancel(reason)` — cancellation with status check
- `MarkPaid()` — payment confirmation
- `MarkCompleted()` — order fulfillment complete

**Status Transitions:**
```
Created → Submitted → StockSecured → ProcessingPayment → Paid → Shipped → Completed
                                                        ↓
                                                     Cancelled
```

### OrderItem (Entity)

| Property | Type | Description |
|---|---|---|
| `Id` | `OrderItemId` | Strongly typed ID |
| `ProductId` | `Guid` | Referenced product |
| `ProductTitle` | `string` | Title snapshot at order time |
| `Quantity` | `int` | Ordered quantity |
| `UnitPrice` | `Money` | Price snapshot at order time |
| `TotalPrice` | `Money` | Computed: Quantity × UnitPrice |

**Design Decision:** Order items snapshot product title and price at order placement. This prevents retroactive price changes from affecting existing orders.

### OrderFulfillmentSaga (Saga State)

The order fulfillment saga orchestrates the complete order lifecycle across multiple bounded contexts. See [MESSAGING_PATTERNS.md](MESSAGING_PATTERNS.md) for the full state machine.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Saga correlation ID (= OrderId) |
| `CustomerId` | `Guid` | Customer identifier |
| `OrderNumber` | `string` | Human-readable order number |
| `TotalAmount` | `Money` | Order total for payment |
| `Items` | `List<OrderItemReservation>` | Items with reservation tracking |
| `State` | `OrderFulfillmentState` | Current saga state (10 values) |
| `IsInventoryReserved` | `bool` | Reservation tracking flag |
| `IsInventoryLockedForPayment` | `bool` | Lock tracking flag |
| `IsPaid` | `bool` | Payment tracking flag |
| `IsInventoryConfirmed` | `bool` | Confirmation tracking flag |
| `PaymentTransactionId` | `Guid?` | Stripe payment intent reference |
| `FailureReason` | `string?` | Cause of failure if applicable |
| `StartedAt` | `DateTime` | Saga start time |
| `CompletedAt` | `DateTime?` | Saga completion time |

## Inventory Context

### Stock (Aggregate Root)

Manages available and reserved inventory quantities with pessimistic locking for concurrent access.

| Property | Type | Description |
|---|---|---|
| `Id` | `StockId` | Strongly typed ID |
| `ProductId` | `Guid` | Associated product |
| `Sku` | `string` | Stock Keeping Unit |
| `AvailableQuantity` | `int` | Currently available for purchase |
| `ReservedQuantity` | `int` | Soft-reserved for pending orders |
| `TotalQuantity` | `int` | Available + Reserved |
| `LowStockThreshold` | `int` | Alert threshold (default: 10) |
| `WarehouseLocation` | `string?` | Physical warehouse location |
| `Version` | `uint` | `xmin` concurrency token |

**Behavior:**
- `Create(productId, sku, initialQuantity, threshold?)` — factory
- `Reserve(quantity, orderId)` — soft reservation, decrements available
- `Lock(reservationId)` — escalate to payment lock
- `Confirm(reservationId)` — deduct from total (permanent)
- `Release(reservationId)` — return to available pool
- `AdjustQuantity(delta, reason)` — manual stock adjustment

**Invariants:**
- Available quantity never goes negative
- Reserved quantity never exceeds total
- Reservations are idempotent per orderId

### StockReservation (Entity)

| Property | Type | Description |
|---|---|---|
| `Id` | `StockReservationId` | Strongly typed ID |
| `StockId` | `StockId` | Parent stock |
| `OrderId` | `Guid` | Reserving order |
| `Quantity` | `int` | Reserved amount |
| `Status` | `ReservationStatus` | `Active`, `PendingPayment`, `Confirmed`, `Released`, `Expired` |
| `CreatedAt` | `DateTime` | Reservation time |
| `ExpiresAt` | `DateTime` | Auto-expiry deadline |

**Reservation Lifecycle:**
```
Active → PendingPayment → Confirmed (permanent deduction)
  ↓           ↓
Released    Released (on payment failure)
  ↓
Expired (by ReservationCleanupJob)
```

See [INVENTORY_PATTERNS.md](INVENTORY_PATTERNS.md) for the detailed reservation pattern.

## Payments Context

### PaymentTransaction (Aggregate Root)

Tracks payment lifecycle with Stripe integration.

| Property | Type | Description |
|---|---|---|
| `Id` | `PaymentTransactionId` | Strongly typed ID |
| `OrderId` | `Guid` | Associated order |
| `ExternalTransactionId` | `string?` | Stripe PaymentIntent ID |
| `Amount` | `Money` | Payment amount |
| `Status` | `PaymentStatus` | `Pending`, `Succeeded`, `Failed`, `Refunded`, `Disputed` |
| `StripeChargeId` | `string?` | Stripe Charge ID (for refunds) |
| `CreatedAt` | `DateTime` | Transaction creation time |
| `CompletedAt` | `DateTime?` | Settlement time |
| `FailureReason` | `string?` | Error details if failed |

### ProcessedWebhookEvent (Entity)

Idempotency store for Stripe webhook deduplication.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Database ID |
| `StripeEventId` | `string` | Stripe event identifier |
| `EventType` | `string` | Stripe event type |
| `PaymentIntentId` | `string?` | Associated payment intent |
| `ProcessedAt` | `DateTime` | Processing timestamp |
| `Status` | `WebhookProcessingStatus` | `Claimed`, `Processed`, `Failed` |

## Shipping Context

### Shipment (Aggregate Root)

Tracks shipment lifecycle from creation to delivery.

| Property | Type | Description |
|---|---|---|
| `Id` | `ShipmentId` | Strongly typed ID |
| `OrderId` | `Guid` | Associated order |
| `TrackingNumber` | `string?` | Courier tracking number |
| `CourierName` | `string` | Shipping courier |
| `Status` | `ShipmentStatus` | `Created`, `Shipped`, `InTransit`, `Delivered`, `Failed` |
| `ShippingAddress` | `Address` | Delivery destination |
| `Items` | `List<ShipmentItem>` | Shipped items |
| `EstimatedDelivery` | `DateTime?` | Estimated delivery date |
| `ActualDelivery` | `DateTime?` | Actual delivery date |

**Design Decision:** The Shipping context uses an Adapter Pattern for courier integrations, allowing pluggable courier implementations.

## Finance Context

### FinancialAuditEntry (Aggregate Root)

Immutable audit trail for all financial operations.

| Property | Type | Description |
|---|---|---|
| `Id` | `FinancialAuditEntryId` | Strongly typed ID |
| `TransactionId` | `string` | External transaction reference |
| `OrderId` | `Guid?` | Associated order |
| `EventType` | `string` | `PaymentReceived`, `RefundIssued`, `DisputeCreated`, etc. |
| `Amount` | `Money` | Transaction amount |
| `Direction` | `TransactionDirection` | `Credit` or `Debit` |
| `Timestamp` | `DateTime` | Event timestamp |
| `Source` | `string` | Event source (`Stripe`, `Internal`, etc.) |
| `Metadata` | `Dictionary<string, string>` | Additional context |

### ReconciliationSession (Aggregate Root)

T+1 daily reconciliation run comparing internal records against external PSP ledger.

| Property | Type | Description |
|---|---|---|
| `Id` | `ReconciliationSessionId` | Strongly typed ID |
| `Date` | `DateTime` | Reconciliation date |
| `Status` | `ReconciliationStatus` | `Pending`, `InProgress`, `Completed`, `Failed` |
| `InternalTransactionCount` | `int` | Internal record count |
| `ExternalTransactionCount` | `int` | PSP record count |
| `MatchedCount` | `int` | Matched transactions |
| `Discrepancies` | `List<ReconciliationDiscrepancy>` | Found discrepancies |
| `CompletedAt` | `DateTime?` | Completion timestamp |

**Discrepancy Types:**
- `MissingExternal` — internal record exists but no PSP record
- `MissingInternal` — PSP record exists but no internal record (ghost charge)
- `AmountMismatch` — amounts differ beyond $0.01 tolerance

See [FINANCIAL_INTEGRITY_MATRIX.md](FINANCIAL_INTEGRITY_MATRIX.md) for the reconciliation algorithm.

## Shared Value Objects

### Money

The primary monetary value object. Default currency is **GEL** (Georgian Lari).

```csharp
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }  // ISO 4217 code

    public static Money Create(decimal amount, string currency = "GEL");
    public Money Add(Money other);       // same currency only
    public Money Subtract(Money other);  // same currency only
    public Money Multiply(int factor);
}
```

**Invariants:**
- Currency codes are 3-letter ISO 4217
- Cross-currency arithmetic throws `InvalidOperationException`
- Monetary amounts use `decimal` for precision

### Address

Postal address value object used by Ordering and Shipping contexts.

### Strongly Typed ID Pattern

All entity identifiers use `IStronglyTypedId<T>`:

```csharp
public interface IStronglyTypedId<TSelf> : IParsable<TSelf>
    where TSelf : struct
{
    Guid Value { get; }
    static abstract TSelf Create(Guid value);
}
```

Implementations are `readonly record struct` with automatic:
- EF Core value converter registration (via `StronglyTypedIdConvention`)
- JSON serialization (via `StronglyTypedIdJsonConverterFactory`)

**All Strongly Typed IDs:**

| Context | ID Type |
|---|---|
| Catalog | `ProductId`, `CategoryId`, `ProductImageId` |
| Ordering | `OrderId`, `OrderItemId` |
| Inventory | `StockId`, `StockReservationId` |
| Payments | `PaymentTransactionId` |
| Shipping | `ShipmentId` |
| Finance | `FinancialAuditEntryId`, `ReconciliationSessionId` |

## Entity Base Classes

### Entity&lt;TId&gt;

```csharp
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : struct
{
    public TId Id { get; protected set; }
    // Equality by ID
}
```

### AggregateRoot&lt;TId&gt;

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected void RaiseDomainEvent(IDomainEvent domainEvent);
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    public void ClearDomainEvents();
}
```

### ValueObject

```csharp
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object> GetEqualityComponents();
    // Structural equality
}
```

## Domain Event Catalog

### Catalog Domain Events

| Event | Raised By | Trigger |
|---|---|---|
| `ProductCreatedEvent` | `Product.Create()` | Product creation |
| `ProductPublishedEvent` | `Product.Publish()` | Publication |
| `ProductPriceChangedEvent` | `Product.UpdatePrice()` | Price change |

### Ordering Domain Events

| Event | Raised By | Trigger |
|---|---|---|
| `OrderCreatedEvent` | `Order.Create()` | Order placement |
| `OrderCancelledEvent` | `Order.Cancel()` | Cancellation |

### Integration Events (Cross-Module)

See [MESSAGING_PATTERNS.md](MESSAGING_PATTERNS.md) for the complete integration event catalog.

## Related Documentation

- [Architecture](ARCHITECTURE.md) — module structure and design principles
- [Messaging Patterns](MESSAGING_PATTERNS.md) — event-driven communication
- [Inventory Patterns](INVENTORY_PATTERNS.md) — reservation lifecycle
- [Financial Integrity](FINANCIAL_INTEGRITY_MATRIX.md) — audit and reconciliation
