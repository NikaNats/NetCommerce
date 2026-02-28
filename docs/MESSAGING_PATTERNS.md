# Messaging Patterns

Complete reference for Wolverine messaging, the order fulfillment saga, integration events, and real-time notifications in NetCommerce.

## Wolverine Configuration

Wolverine serves as the in-process message bus with PostgreSQL-backed transactional outbox. Configuration is in `Program.cs`:

```csharp
builder.UseWolverineMessaging(opts =>
{
    opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static; // Native AOT
}, additionalConfig: opts =>
{
    // Handler assembly discovery
    opts.Discovery.IncludeAssembly(typeof(CreateProductCommand).Assembly);   // Catalog
    opts.Discovery.IncludeAssembly(typeof(ReserveStockCommand).Assembly);    // Inventory
    opts.Discovery.IncludeAssembly(typeof(CreateOrderCommand).Assembly);     // Ordering
    opts.Discovery.IncludeAssembly(typeof(RefundPaymentTransactionCommand).Assembly); // Payments
    opts.Discovery.IncludeAssembly(typeof(CheckDailyReconciliation).Assembly);        // Finance
});
```

### Key Design Decisions

| Decision | Rationale |
|---|---|
| `TypeLoadMode.Static` | Pre-generated handler code for Native AOT — no runtime reflection |
| PostgreSQL outbox | Same database as domain state — true transactional guarantees |
| Cascading messages | Handler return values auto-publish as new messages |
| `[Transactional]` middleware | Auto-commits EF Core changes + outbox in single transaction |
| Dead letter queue | Failed messages routed to DLQ after retry exhaustion |

## Integration Events

Integration events flow between bounded contexts via the Wolverine transactional outbox. All events are defined in `src/Domain.Shared/NetCommerce.Domain.Shared/Events/`.

### Event Catalog

| Event | Publisher | Consumers | Payload |
|---|---|---|---|
| `OrderSubmittedIntegrationEvent` | Ordering | Inventory | OrderId, OrderNumber, CustomerId |
| `OrderGracePeriodConfirmedIntegrationEvent` | Ordering | Inventory | OrderId, OrderNumber, CustomerId, TotalAmount (Money) |
| `OrderStockConfirmedIntegrationEvent` | Ordering | Payments | OrderId |
| `OrderPaidIntegrationEvent` | Payments | Inventory, Shipping | OrderId, OrderNumber, TotalAmount (Money) |
| `OrderPlacedIntegrationEvent` | Ordering | Notifications | OrderId, OrderNumber, CustomerEmail, CustomerName, TotalAmount |
| `OrderCancelledIntegrationEvent` | Ordering | Inventory | OrderId, Reason, PreviousStatus |
| `PaymentCompletedIntegrationEvent` | Payments | Finance | ExternalTransactionId, OrderId, Amount (Money) |
| `OrderInventoryConfirmationFailedIntegrationEvent` | Inventory | Ordering | OrderId, PaymentTransactionId, Amount, FailureReason, FailureDetails |
| `StockReservedIntegrationEvent` | Inventory | — | StockId, ProductId, OrderId, Quantity, RemainingAvailable |
| `StockDeductedIntegrationEvent` | Inventory | — | StockId, ProductId, OrderId, Quantity, NewTotal |
| `StockReservationReleasedIntegrationEvent` | Inventory | — | StockId, ProductId, OrderId, Quantity, NewAvailable |

### Shipping Events

| Event | Publisher | Consumers | Payload |
|---|---|---|---|
| `ShipmentCreatedIntegrationEvent` | Shipping | Ordering | ShipmentId, OrderId, TrackingNumber |
| `ShipmentCreationFailedEvent` | Shipping | Ordering | OrderId, Reason |
| `ShipmentDeliveredIntegrationEvent` | Shipping | Ordering | ShipmentId, OrderId |

## Order Fulfillment Saga

The `OrderFulfillmentSaga` orchestrates the complete order lifecycle across Inventory, Payments, and Ordering contexts. It uses Wolverine's saga persistence (PostgreSQL-backed) with explicit state management.

### State Machine

| State | Value | Description |
|---|---|---|
| `NotStarted` | 0 | Initial state |
| `ReservingInventory` | 1 | Awaiting stock reservation |
| `InGracePeriod` | 2 | Customer cancellation window (5 min) |
| `LockingInventory` | 3 | Escalating reservation to payment lock |
| `ProcessingPayment` | 4 | Awaiting Stripe webhook |
| `ConfirmingInventory` | 5 | Awaiting stock deduction confirmation |
| `Compensating` | 6 | Executing rollback (refund + release) |
| `Completed` | 7 | Successfully fulfilled |
| `Failed` | 8 | Terminal failure state |
| `ManualInterventionRequired` | 9 | Requires admin action |

### Saga Messages

#### Commands (Saga → Modules)

| Command | Target Module | Purpose |
|---|---|---|
| `StartOrderFulfillmentCommand` | Saga | Initiates the saga |
| `ReserveInventoryCommand` | Inventory | Reserve stock for order items |
| `LockInventoryForPaymentCommand` | Inventory | Escalate to payment lock |
| `ConfirmInventoryCommand` | Inventory | Permanently deduct stock |
| `ReleaseInventoryReservationCommand` | Inventory | Release reserved stock |
| `RequestPaymentCommand` | Payments | Initiate Stripe payment |
| `RefundPaymentCommand` | Payments | Refund on compensation |
| `FinalizeOrderCommand` | Ordering | Mark order completed |
| `FailOrderCommand` | Ordering | Mark order failed |

#### Events (Modules → Saga)

All events carry `[SagaIdentity]` on `OrderId` for correlation:

| Event | Source Module | Purpose |
|---|---|---|
| `InventoryReserved` | Inventory | Stock successfully reserved |
| `InventoryReservationFailed` | Inventory | Insufficient stock |
| `InventoryLocked` | Inventory | Lock for payment acquired |
| `PaymentInitiated` | Payments | Stripe PaymentIntent created |
| `PaymentSucceeded` | Payments | Stripe webhook confirmed payment |
| `PaymentFailed` | Payments | Payment rejected |
| `InventoryConfirmed` | Inventory | Stock permanently deducted |
| `InventoryConfirmationFailed` | Inventory | Confirmation error |
| `RefundCompleted` | Payments | Refund processed |
| `RefundFailed` | Payments | Refund failed — needs manual intervention |

#### Timeout Messages

| Timeout | Duration | Trigger |
|---|---|---|
| `InventoryReservationTimeoutMessage` | 5 min | Stuck in ReservingInventory |
| `GracePeriodTimeout` | 5 min | Grace period elapsed |
| `PaymentTimeoutMessage` | 30 min | No Stripe webhook received |
| `InventoryConfirmationTimeoutMessage` | 5 min | Stuck in ConfirmingInventory |
| `CompensationStalledTimeoutMessage` | 4 hours | Refund not completing |

### Happy Path Flow

```
1. StartOrderFulfillmentCommand
   → State: ReservingInventory
   → Sends: ReserveInventoryCommand + InventoryReservationTimeoutMessage (5min)

2. InventoryReserved
   → State: InGracePeriod
   → Sends: GracePeriodTimeout (5min), OrderStatusChanged("StockSecured")

3. GracePeriodTimeout
   → State: LockingInventory
   → Sends: LockInventoryForPaymentCommand, OrderStatusChanged("ProcessingPayment")

4. InventoryLocked
   → State: ProcessingPayment
   → Sends: RequestPaymentCommand + PaymentTimeoutMessage (30min)

5. PaymentInitiated
   → Stores ExternalTransactionId, awaits webhook

6. PaymentSucceeded (from Stripe webhook)
   → State: ConfirmingInventory
   → Sends: ConfirmInventoryCommand + InventoryConfirmationTimeoutMessage (5min)

7. InventoryConfirmed
   → State: Completed
   → Sends: FinalizeOrderCommand, OrderStatusChanged("Success")
   → MarkCompleted()
```

### Compensation Flows

#### Payment Failure

```
PaymentFailed
  → State: Failed
  → Sends: ReleaseInventoryReservationCommand, FailOrderCommand
  → MarkCompleted()
```

#### Inventory Confirmation Failure

```
InventoryConfirmationFailed
  → State: Compensating
  → Sends: RefundPaymentCommand, ReleaseInventoryReservationCommand,
           FailOrderCommand, CompensationStalledTimeoutMessage (4h)
  → Saga stays open (NOT completed — awaits refund result)

RefundCompleted
  → State: Failed
  → MarkCompleted()

RefundFailed
  → State: ManualInterventionRequired
  → Saga persists for admin intervention
```

#### Timeout Escalation

All timeout handlers are **idempotent** — they check current state before acting:

```
InventoryReservationTimeoutMessage
  → Only acts if still in ReservingInventory
  → Sends: FailOrderCommand, MarkCompleted()

PaymentTimeoutMessage
  → Only acts if still in ProcessingPayment
  → Sends: ReleaseInventoryReservationCommand, FailOrderCommand, MarkCompleted()

CompensationStalledTimeoutMessage
  → Only acts if still in Compensating (after 4h)
  → State: ManualInterventionRequired
```

### NotFound Handlers

The saga defines 11 `NotFound` handlers for messages that arrive after the saga is completed or deleted. These prevent Wolverine from throwing exceptions on late-arriving messages:

```csharp
public static void NotFound(InventoryReserved message, ILogger logger)
    => logger.LogWarning("Late InventoryReserved for completed saga {OrderId}", message.OrderId);
```

Handled message types: `InventoryReserved`, `InventoryLocked`, `InventoryReservationFailed`, `PaymentSucceeded`, `PaymentFailed`, `InventoryConfirmed`, `InventoryConfirmationFailed`, all 4 timeout messages.

## Stripe Webhook Events

The payment webhook endpoint processes these Stripe event types:

| Stripe Event | Command Dispatched | Description |
|---|---|---|
| `payment_intent.succeeded` | `ProcessExternalPaymentConfirmation` | Payment confirmed |
| `payment_intent.payment_failed` | `ProcessExternalPaymentConfirmation` | Payment rejected |
| `payment_intent.canceled` | `ProcessExternalPaymentConfirmation` | Payment cancelled |
| `charge.refunded` | `ProcessStripeRefundWebhook` | Refund processed |
| `charge.dispute.created` | `ProcessStripeDisputeCreated` | Dispute opened |
| `charge.dispute.updated` | `ProcessStripeDisputeUpdated` | Dispute status change |
| `charge.dispute.closed` | `ProcessStripeDisputeUpdated` | Dispute resolved |

See [WEBHOOK_REFERENCE.md](WEBHOOK_REFERENCE.md) for the complete webhook integration specification.

## Real-Time Notifications

Order status updates are pushed to clients via SignalR:

```csharp
public record OrderStatusChanged(Guid OrderId, string Status, string Message) : IOrderNotification;
```

The SignalR hub is mapped at `/api/messages`. Status messages are published as cascading saga messages:

| Status | Trigger |
|---|---|
| `"StockSecured"` | Inventory reserved |
| `"ProcessingPayment"` | Grace period elapsed |
| `"Success"` | Order completed |
| `"Error"` | Order failed |

## Wolverine Handler Pattern

Handlers are static classes with the `[WolverineHandler]` attribute:

```csharp
[WolverineHandler]
public static class OrderSubmittedHandler
{
    // Return value becomes a cascading message
    public static ReserveInventoryCommand Handle(
        OrderSubmittedIntegrationEvent @event,
        ILogger logger)
    {
        logger.LogInformation("Order submitted: {OrderId}", @event.OrderId);
        return new ReserveInventoryCommand(@event.OrderId, ...);
    }
}
```

### Cascading Messages

Handler return values are automatically published via the transactional outbox:

| Return Type | Behavior |
|---|---|
| Single message | Published as one outgoing message |
| `IEnumerable<object>` | Each item published as separate message |
| `void` / `Task` | No cascading message |
| `OutgoingMessages` | Explicit control over outgoing messages |

### Transactional Outbox

Messages are persisted in the same database transaction as domain state changes:

```
BEGIN TRANSACTION
  1. Save domain entity changes (EF Core)
  2. Save outgoing messages to wolverine_outgoing_envelopes
COMMIT

Wolverine polls outgoing_envelopes and delivers asynchronously
```

This guarantees at-least-once delivery. Handlers must be **idempotent**.

## Dead Letter Queue

Messages that fail after retry exhaustion are routed to the dead letter queue. Admin endpoints provide management:

| Endpoint | Method | Description |
|---|---|---|
| `GET /api/admin/dlq` | GET | List dead-lettered messages |
| `POST /api/admin/dlq/{id}/replay` | POST | Replay a single message |
| `DELETE /api/admin/dlq/{id}` | DELETE | Discard a message |
| `POST /api/admin/dlq/bulk-replay` | POST | Replay messages by type filter |

See [OPERATIONS.md](OPERATIONS.md) for DLQ monitoring procedures.

## Module Handler Registry

### Catalog Handlers

| Handler | Message | Action |
|---|---|---|
| `CreateProductHandler` | `CreateProductCommand` | Create product aggregate |
| `UpdateProductHandler` | `UpdateProductCommand` | Update product details |
| `ArchiveProductHandler` | `ArchiveProductCommand` | Soft-delete product |
| `ProductCacheInvalidationHandler` | Domain events | Evict HybridCache + MeiliSearch sync |

### Inventory Handlers

| Handler | Message | Action |
|---|---|---|
| `OrderSubmittedHandler` | `OrderSubmittedIntegrationEvent` | Initiate soft reservation |
| `OrderPaidHandler` | `OrderPaidIntegrationEvent` | Confirm reservations |
| `OrderCancelledHandler` | `OrderCancelledIntegrationEvent` | Release reservations |
| `StockCommandHandlers` | `ReserveStockCommand` | Pessimistic lock + reserve |
| `StockCommandHandlers` | `ConfirmReservationCommand` | Deduct from total |
| `StockCommandHandlers` | `ReleaseReservationCommand` | Return to available |

### Payments Handlers

| Handler | Message | Action |
|---|---|---|
| `ProcessExternalPaymentConfirmationHandler` | `ProcessExternalPaymentConfirmation` | Update transaction status |
| `RefundPaymentHandler` | `RefundPaymentCommand` | Initiate Stripe refund |
| `ProcessStripeRefundHandler` | `ProcessStripeRefundWebhook` | Record refund |
| `ProcessStripeDisputeCreatedHandler` | `ProcessStripeDisputeCreated` | Record dispute |
| `ProcessStripeDisputeUpdatedHandler` | `ProcessStripeDisputeUpdated` | Update dispute status |

### Finance Handlers

| Handler | Message | Action |
|---|---|---|
| `CheckDailyReconciliationHandler` | `CheckDailyReconciliation` | Trigger T+1 reconciliation |
| `ResolveDiscrepancyHandler` | `ResolveDiscrepancyCommand` | Resolve reconciliation discrepancy |

## Related Documentation

- [Architecture](ARCHITECTURE.md) — module boundaries and design principles
- [Architecture Diagrams](ARCHITECTURE_DIAGRAMS.md) — visual flow diagrams
- [Webhook Reference](WEBHOOK_REFERENCE.md) — Stripe webhook processing
- [Operations](OPERATIONS.md) — DLQ management and monitoring
- [Financial Integrity](FINANCIAL_INTEGRITY_MATRIX.md) — reconciliation engine
