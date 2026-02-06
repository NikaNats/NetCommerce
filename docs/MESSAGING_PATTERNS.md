# NetCommerce Messaging Patterns

> **Comprehensive guide to Wolverine messaging, sagas, and event-driven architecture**

---

## Table of Contents

1. [Messaging Overview](#messaging-overview)
2. [Wolverine Configuration](#wolverine-configuration)
3. [Message Types](#message-types)
4. [Handler Patterns](#handler-patterns)
5. [Transactional Outbox](#transactional-outbox)
6. [Saga Pattern (Process Manager)](#saga-pattern-process-manager)
7. [Compensating Transactions](#compensating-transactions)
8. [Error Handling & Dead Letter Queue](#error-handling--dead-letter-queue)
9. [Message Serialization](#message-serialization)
10. [Idempotency](#idempotency)
11. [Monitoring & Debugging](#monitoring--debugging)

---

## Messaging Overview

NetCommerce uses **Wolverine** as its messaging framework, providing:

- **Transactional Outbox**: Atomic message publishing with database changes
- **Durable Inbox**: At-least-once delivery guarantee
- **Saga Support**: Long-running process orchestration
- **Local Queues**: In-process message handling
- **External Transport**: Future RabbitMQ/Azure Service Bus support

### Why Wolverine?

| Feature | Benefit |
|---------|---------|
| Transactional Outbox | Messages never lost, even on crashes |
| Cascading Messages | Handlers can return messages to publish |
| Static Handler Classes | No dependency injection ceremony |
| EF Core Integration | Seamless database transactions |
| Minimal Ceremony | Convention-based, low boilerplate |

### Message Flow Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           MESSAGE FLOW                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────┐                                                           │
│  │   API       │                                                           │
│  │  Endpoint   │                                                           │
│  └──────┬──────┘                                                           │
│         │ IMessageBus.InvokeAsync<T>()                                     │
│         ▼                                                                   │
│  ┌─────────────────────────────────────────────────────────────────┐       │
│  │                    WOLVERINE PIPELINE                            │       │
│  │                                                                  │       │
│  │  ┌───────────┐   ┌───────────┐   ┌───────────┐   ┌───────────┐ │       │
│  │  │ Middleware│──▶│ Middleware│──▶│  Handler  │──▶│ Middleware│ │       │
│  │  │ (Audit)   │   │ (Logging) │   │ (Business)│   │ (Outbox)  │ │       │
│  │  └───────────┘   └───────────┘   └───────────┘   └───────────┘ │       │
│  │                                         │                       │       │
│  │                                         │ Cascading Messages   │       │
│  │                                         ▼                       │       │
│  │                              ┌───────────────────┐              │       │
│  │                              │   Outbox Table    │              │       │
│  │                              │ (Same Transaction)│              │       │
│  │                              └─────────┬─────────┘              │       │
│  └──────────────────────────────────────────┼──────────────────────┘       │
│                                              │                             │
│                                              │ Agent polls                 │
│                                              ▼                             │
│  ┌─────────────────────────────────────────────────────────────────┐       │
│  │                    CONSUMER MODULE                               │       │
│  │                                                                  │       │
│  │  ┌───────────────┐    ┌───────────────┐    ┌───────────────┐   │       │
│  │  │ Inbox Check   │───▶│   Handler     │───▶│   Database    │   │       │
│  │  │ (Dedup)       │    │  (Business)   │    │   Changes     │   │       │
│  │  └───────────────┘    └───────────────┘    └───────────────┘   │       │
│  │                                                                  │       │
│  └─────────────────────────────────────────────────────────────────┘       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Wolverine Configuration

### Kernel Defaults

```csharp
// src/Kernel.Adapters/NetCommerce.Kernel.Wolverine/WolverineKernelExtensions.cs

public static WolverineOptions ConfigureKernelDefaults<TDbContext>(this WolverineOptions opts)
    where TDbContext : DbContext
{
    // 1. TRANSACTIONAL OUTBOX
    // Ensures DB changes and outgoing messages are atomic
    opts.UseEntityFrameworkCoreTransactions();

    // 2. MESSAGE IDENTITY (Critical for Modular Monolith)
    // Uses both ID and Destination to handle same message going to multiple handlers
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

    // 3. DURABLE QUEUES
    // All listeners use persistent inbox for at-least-once delivery
    opts.Policies.UseDurableInboxOnAllListeners();
    opts.Policies.UseDurableLocalQueues();

    // 4. DEAD LETTER QUEUE EXPIRATION
    // Prevents database bloat, retains for audit compliance
    opts.Durability.DeadLetterQueueExpirationEnabled = true;
    opts.Durability.DeadLetterQueueExpiration = TimeSpan.FromDays(30);

    // 5. AUDIT MIDDLEWARE
    // Applied to messages implementing IAuditableCommand
    opts.Policies.AddMiddleware(typeof(AuditMiddleware));

    // 6. SERIALIZATION (Pure Canonical)
    // Legacy type resolution removed in Phase 6
    opts.UseSystemTextJsonForSerialization(options =>
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.WriteIndented = false;
        // TypeInfoResolver configured at API layer via ConfigureHttpJsonOptions
    });

    return opts;
}
```

### Registration in API

```csharp
// src/Api/Program.cs

builder.Host.UseWolverine(opts =>
{
    opts.ConfigureKernelDefaults<OrderingDbContext>();

    // Discover handlers in all module assemblies
    opts.Discovery.IncludeAssembly(typeof(OrderingModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(InventoryModule).Assembly);
    opts.Discovery.IncludeAssembly(typeof(PaymentsModule).Assembly);

    // Configure PostgreSQL durability
    opts.PersistMessagesWithPostgresql(connectionString, "wolverine");

    // Local queue settings
    opts.LocalQueue("default")
        .UseDurableInbox()
        .Sequential(); // Process one at a time (can change for parallelism)
});
```

---

## Message Types

### Commands

Commands represent intentions to change state. They are named imperatively.

```csharp
/// <summary>
/// Command to create a new order.
/// Commands should be immutable records.
/// </summary>
public sealed record CreateOrderCommand(
    Guid CustomerId,
    ShippingAddress ShippingAddress,
    List<OrderItemDto> Items,
    string IdempotencyKey) : IAuditableCommand
{
    public string CommandName => "CreateOrder";
    public Guid? UserId => CustomerId;
}

/// <summary>
/// Command to reserve inventory for an order.
/// </summary>
public sealed record ReserveInventoryCommand(
    Guid OrderId,
    List<OrderItemReservation> Items);

/// <summary>
/// Command to process payment.
/// </summary>
public sealed record ProcessPaymentCommand(
    Guid OrderId,
    Money Amount,
    string IdempotencyKey);
```

### Queries

Queries retrieve data without side effects.

```csharp
/// <summary>
/// Query to get product by ID.
/// </summary>
public sealed record GetProductByIdQuery(Guid ProductId);

/// <summary>
/// Query with pagination.
/// </summary>
public sealed record GetOrdersQuery(
    Guid? CustomerId,
    OrderStatus? Status,
    int Page = 1,
    int PageSize = 20);
```

### Integration Events

Events notify other modules about something that happened.

```csharp
/// <summary>
/// Integration event: Order has been submitted and needs inventory reservation.
/// Published from Ordering module, consumed by Inventory module.
/// </summary>
public sealed record OrderSubmittedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    List<OrderItemReservation> Items,
    Money TotalAmount) : IntegrationEvent;

/// <summary>
/// Integration event: Inventory has been reserved.
/// Published from Inventory module, consumed by Ordering saga.
/// </summary>
public sealed record InventoryReservedIntegrationEvent(
    Guid OrderId,
    List<ReservedItemDto> ReservedItems) : IntegrationEvent;

/// <summary>
/// Integration event: Payment has succeeded.
/// Published from Payments module, consumed by Ordering saga.
/// </summary>
public sealed record PaymentSucceededIntegrationEvent(
    Guid OrderId,
    string TransactionId,
    Money Amount) : IntegrationEvent;
```

### Saga Messages

Messages used within saga orchestration.

```csharp
/// <summary>
/// Command to start the order fulfillment saga.
/// </summary>
public sealed record StartOrderFulfillmentCommand(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    Money TotalAmount,
    List<OrderItemReservation> Items);

/// <summary>
/// Timeout message for reservation expiry.
/// </summary>
public sealed record InventoryReservationTimeoutMessage(Guid OrderId);
```

---

## Handler Patterns

### Static Handler Class

Wolverine prefers static handler classes with `[WolverineHandler]` attribute:

```csharp
/// <summary>
/// Handler for CreateOrderCommand.
/// Static class, dependencies passed as parameters.
/// </summary>
[WolverineHandler]
public static class CreateOrderHandler
{
    /// <summary>
    /// Convention: Method named Handle with message as first parameter.
    /// Additional parameters are resolved from DI.
    /// </summary>
    public static async Task<Result<Guid>> Handle(
        CreateOrderCommand command,
        IOrderRepository repository,
        ILogger<CreateOrderCommand> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Creating order for customer {CustomerId}",
            command.CustomerId);

        var addressResult = ShippingAddress.Create(
            command.ShippingAddress.Street,
            command.ShippingAddress.City,
            command.ShippingAddress.State,
            command.ShippingAddress.PostalCode,
            command.ShippingAddress.Country);

        if (addressResult.IsFailure)
            return Result.Failure<Guid>(addressResult.Error);

        var order = Order.Create(
            command.CustomerId,
            addressResult.Value,
            command.IdempotencyKey);

        foreach (var item in command.Items)
        {
            order.AddItem(item.ProductId, item.ProductName, item.Price, item.Quantity);
        }

        await repository.AddAsync(order, cancellationToken);

        logger.LogInformation(
            "Order {OrderNumber} created with ID {OrderId}",
            order.OrderNumber,
            order.Id);

        return Result.Success(order.Id);
    }
}
```

### Cascading Messages

Handlers can return messages to be published:

```csharp
[WolverineHandler]
public static class OrderSubmittedDomainEventHandler
{
    /// <summary>
    /// Return value becomes a cascading message.
    /// Published via the transactional outbox.
    /// </summary>
    public static OrderSubmittedIntegrationEvent Handle(
        OrderSubmittedDomainEvent @event,
        IOrderRepository repository)
    {
        var order = repository.GetById(@event.OrderId);

        // Return integration event - Wolverine publishes it
        return new OrderSubmittedIntegrationEvent(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.Items.Select(i => new OrderItemReservation(
                i.ProductId,
                i.Quantity)).ToList(),
            order.TotalAmount);
    }
}
```

### Multiple Cascading Messages

Return an `IEnumerable` or tuple for multiple messages:

```csharp
[WolverineHandler]
public static class PaymentSucceededHandler
{
    public static (ConfirmInventoryCommand, SendOrderConfirmationEmail) Handle(
        PaymentSucceededIntegrationEvent @event,
        IOrderRepository repository)
    {
        var order = repository.GetById(@event.OrderId);

        return (
            new ConfirmInventoryCommand(order.Id, order.ReservedItems),
            new SendOrderConfirmationEmail(order.CustomerId, order.OrderNumber)
        );
    }
}
```

### Query Handlers

```csharp
[WolverineHandler]
public static class GetProductByIdHandler
{
    public static async Task<Result<ProductDto>> Handle(
        GetProductByIdQuery query,
        IProductRepository repository,
        CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(
            query.ProductId,
            cancellationToken);

        return product is null
            ? Result.Failure<ProductDto>(Error.NotFound("Product", query.ProductId))
            : Result.Success(product.ToDto());
    }
}
```

---

## Transactional Outbox

### How It Works

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    TRANSACTIONAL OUTBOX PATTERN                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  STEP 1: Handler executes within transaction                                │
│                                                                             │
│    BEGIN TRANSACTION                                                        │
│    ├── INSERT INTO orders (...)                                            │
│    ├── INSERT INTO wolverine.outgoing_envelopes (message_type, body, ...)  │
│    └── COMMIT                                                               │
│                                                                             │
│  STEP 2: Wolverine agent polls outbox                                       │
│                                                                             │
│    SELECT * FROM wolverine.outgoing_envelopes                              │
│    WHERE status = 'pending'                                                 │
│    FOR UPDATE SKIP LOCKED  -- Concurrent processing safe                   │
│                                                                             │
│  STEP 3: Agent delivers message                                             │
│                                                                             │
│    ├── Route to local handler OR                                           │
│    ├── Send to external transport (RabbitMQ, etc.)                        │
│    └── Mark envelope as processed                                          │
│                                                                             │
│  BENEFITS:                                                                   │
│    ✓ No message loss (even on crash between DB commit and send)            │
│    ✓ Exactly-once semantics (with inbox)                                   │
│    ✓ Automatic retry on transient failures                                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Database Tables

```sql
-- Outbox: Messages to be sent
CREATE TABLE wolverine.wolverine_outgoing_envelopes (
    id UUID PRIMARY KEY,
    destination VARCHAR(255),
    message_type VARCHAR(500),
    body BYTEA,
    scheduled_time TIMESTAMPTZ,
    sent_at TIMESTAMPTZ,
    status VARCHAR(50),
    attempts INT DEFAULT 0
);

-- Inbox: Received messages (for deduplication)
CREATE TABLE wolverine.wolverine_incoming_envelopes (
    id UUID PRIMARY KEY,
    source VARCHAR(255),
    message_type VARCHAR(500),
    body BYTEA,
    received_at TIMESTAMPTZ,
    status VARCHAR(50)
);
```

### Configuration

```csharp
opts.PersistMessagesWithPostgresql(connectionString, "wolverine");

// Outbox processing interval
opts.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(5);

// Recovery timeout (how long to wait before reprocessing stuck messages)
opts.Durability.RecoveryBatchSize = 100;
opts.Durability.FirstNodeReassignmentExecution = TimeSpan.FromSeconds(30);
```

---

## Saga Pattern (Process Manager)

### Order Fulfillment Saga

The `OrderFulfillmentSaga` orchestrates the order fulfillment workflow:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ORDER FULFILLMENT SAGA                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────┐                                                           │
│  │ NotStarted  │                                                           │
│  └──────┬──────┘                                                           │
│         │ StartOrderFulfillmentCommand                                     │
│         ▼                                                                   │
│  ┌─────────────────────┐      ReserveInventoryCommand                     │
│  │ ReservingInventory  │ ────────────────────────────────▶ Inventory      │
│  └──────┬──────────────┘                                     Module       │
│         │                                                                   │
│         │ InventoryReservedEvent                                           │
│         ▼                                                                   │
│  ┌─────────────────────┐                                                   │
│  │   InGracePeriod     │◀──── 15-minute cancellation window               │
│  │   (Awaiting)        │                                                   │
│  └──────┬──────────────┘                                                   │
│         │                                                                   │
│         │ GracePeriodConfirmedEvent (timeout or explicit)                 │
│         ▼                                                                   │
│  ┌─────────────────────┐      ProcessPaymentCommand                       │
│  │ ProcessingPayment   │ ────────────────────────────────▶ Payments       │
│  └──────┬──────────────┘                                     Module       │
│         │                                                                   │
│    ┌────┴────┐                                                             │
│    │         │                                                             │
│    │ Success │ Failure                                                     │
│    ▼         ▼                                                             │
│  ┌───────────────────┐  ┌───────────────────┐                             │
│  │ConfirmingInventory│  │   Compensating    │                             │
│  └─────────┬─────────┘  │ (Release Stock)   │                             │
│            │            └─────────┬─────────┘                             │
│            │ ConfirmInventory     │                                        │
│            ▼                      ▼                                        │
│  ┌───────────────────┐  ┌───────────────────┐                             │
│  │    Completed      │  │      Failed       │                             │
│  │  (Saga Deleted)   │  │  (With Reason)    │                             │
│  └───────────────────┘  └───────────────────┘                             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Saga Implementation

```csharp
/// <summary>
/// Order fulfillment saga - persisted by Wolverine to PostgreSQL.
/// </summary>
public sealed class OrderFulfillmentSaga : Saga
{
    #region State (Persisted to Database)

    public Guid Id { get; set; }  // Saga correlation ID = Order ID
    public Guid CustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Money TotalAmount { get; set; } = Money.Zero();
    public List<OrderItemReservation> Items { get; set; } = [];
    public OrderFulfillmentState State { get; set; } = OrderFulfillmentState.NotStarted;

    // Compensation data
    public bool IsInventoryReserved { get; set; }
    public bool IsPaid { get; set; }
    public string? PaymentTransactionId { get; set; }
    public List<ReservedItem>? ReservedItems { get; set; }
    public string? FailureReason { get; set; }

    #endregion

    #region Saga Initiation

    /// <summary>
    /// Start method: Creates saga state and returns initial commands.
    /// Convention: Static method named Start.
    /// </summary>
    public static (
        OrderFulfillmentSaga Saga,
        ReserveInventoryCommand ReserveCommand,
        InventoryReservationTimeoutMessage Timeout
    ) Start(
        StartOrderFulfillmentCommand command,
        ILogger<OrderFulfillmentSaga> logger)
    {
        logger.LogInformation(
            "Starting saga for Order {OrderId}", command.OrderId);

        var saga = new OrderFulfillmentSaga
        {
            Id = command.OrderId,
            CustomerId = command.CustomerId,
            OrderNumber = command.OrderNumber,
            TotalAmount = command.TotalAmount,
            Items = command.Items.ToList(),
            State = OrderFulfillmentState.ReservingInventory,
            StartedAt = DateTime.UtcNow
        };

        var reserveCommand = new ReserveInventoryCommand(
            command.OrderId,
            command.Items);

        // Schedule timeout for reservation expiry
        var timeout = new InventoryReservationTimeoutMessage(command.OrderId)
            .ScheduleFor(TimeSpan.FromMinutes(15));

        return (saga, reserveCommand, timeout);
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handle successful inventory reservation.
    /// </summary>
    public void Handle(
        InventoryReservedIntegrationEvent @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        if (State != OrderFulfillmentState.ReservingInventory)
        {
            logger.LogWarning("Received InventoryReserved in unexpected state {State}", State);
            return;
        }

        IsInventoryReserved = true;
        ReservedItems = @event.ReservedItems;
        State = OrderFulfillmentState.InGracePeriod;

        logger.LogInformation(
            "Inventory reserved for Order {OrderId}, entering grace period",
            Id);
    }

    /// <summary>
    /// Handle grace period confirmation - proceed to payment.
    /// Returns cascading command.
    /// </summary>
    public ProcessPaymentCommand Handle(
        OrderGracePeriodConfirmedIntegrationEvent @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        if (State != OrderFulfillmentState.InGracePeriod)
        {
            logger.LogWarning("Received GracePeriodConfirmed in unexpected state {State}", State);
            return null!; // Wolverine ignores null returns
        }

        State = OrderFulfillmentState.ProcessingPayment;

        logger.LogInformation(
            "Grace period ended for Order {OrderId}, processing payment",
            Id);

        return new ProcessPaymentCommand(
            Id,
            TotalAmount,
            $"order-{Id}");
    }

    /// <summary>
    /// Handle successful payment - confirm inventory and complete.
    /// </summary>
    public ConfirmInventoryCommand Handle(
        PaymentSucceededIntegrationEvent @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        IsPaid = true;
        PaymentTransactionId = @event.TransactionId;
        State = OrderFulfillmentState.ConfirmingInventory;

        logger.LogInformation(
            "Payment succeeded for Order {OrderId}, confirming inventory",
            Id);

        return new ConfirmInventoryCommand(Id, ReservedItems!);
    }

    /// <summary>
    /// Handle inventory confirmation - saga complete!
    /// Returning null completes and deletes the saga.
    /// </summary>
    public void Handle(
        InventoryConfirmedIntegrationEvent @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        State = OrderFulfillmentState.Completed;
        CompletedAt = DateTime.UtcNow;

        logger.LogInformation(
            "Order {OrderId} fulfillment completed successfully",
            Id);

        // Mark saga as complete - Wolverine deletes it
        MarkCompleted();
    }

    #endregion

    #region Compensation Handlers

    /// <summary>
    /// Handle payment failure - release inventory.
    /// </summary>
    public ReleaseInventoryCommand Handle(
        PaymentFailedIntegrationEvent @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        State = OrderFulfillmentState.Compensating;
        FailureReason = @event.Reason;

        logger.LogWarning(
            "Payment failed for Order {OrderId}: {Reason}. Releasing inventory.",
            Id, @event.Reason);

        return new ReleaseInventoryCommand(Id, ReservedItems!);
    }

    /// <summary>
    /// Handle inventory released (compensation complete).
    /// </summary>
    public void Handle(
        InventoryReleasedIntegrationEvent @event,
        ILogger<OrderFulfillmentSaga> logger)
    {
        State = OrderFulfillmentState.Failed;
        CompletedAt = DateTime.UtcNow;

        logger.LogInformation(
            "Compensation complete for Order {OrderId}. Saga failed: {Reason}",
            Id, FailureReason);

        MarkCompleted();
    }

    #endregion
}

public enum OrderFulfillmentState
{
    NotStarted,
    ReservingInventory,
    InGracePeriod,
    ProcessingPayment,
    ConfirmingInventory,
    Compensating,
    ManualInterventionRequired,
    Completed,
    Failed
}
```

### Saga Persistence

Sagas are persisted to the `wolverine.saga_state` table:

```sql
CREATE TABLE wolverine.saga_state (
    id UUID PRIMARY KEY,
    saga_type VARCHAR(500),
    state JSONB,  -- Serialized saga state
    created_at TIMESTAMPTZ,
    updated_at TIMESTAMPTZ
);

-- Example query to find stuck sagas
SELECT id, saga_type, state->>'State', state->>'FailureReason'
FROM wolverine.saga_state
WHERE state->>'State' = 'ManualInterventionRequired';
```

---

## Compensating Transactions

### The Problem

Distributed transactions across modules are impractical. Instead, we use compensating transactions to undo changes when later steps fail.

### Compensation Strategy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    COMPENSATION STRATEGY                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  SCENARIO: Payment succeeds, but inventory confirmation fails               │
│  (e.g., stock was modified by another process)                             │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │ State: ConfirmingInventory                                        │      │
│  │ - Inventory reserved ✓                                           │      │
│  │ - Payment taken ✓                                                │      │
│  │ - Inventory confirmation FAILED ✗                                │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                              │                                             │
│                              │ InventoryConfirmationFailed                 │
│                              ▼                                             │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │ COMPENSATION SEQUENCE:                                            │      │
│  │                                                                   │      │
│  │ 1. Issue RefundPaymentCommand                                    │      │
│  │    └── Payments module processes refund                          │      │
│  │                                                                   │      │
│  │ 2. Issue ReleaseInventoryCommand                                 │      │
│  │    └── Inventory module releases reservation                     │      │
│  │                                                                   │      │
│  │ 3. Update order status to Cancelled/Refunded                     │      │
│  │    └── Ordering module updates order                             │      │
│  │                                                                   │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                              │                                             │
│                    ┌─────────┴─────────┐                                   │
│                    │                   │                                   │
│             RefundSucceeded      RefundFailed                              │
│                    ▼                   ▼                                   │
│  ┌─────────────────────┐  ┌─────────────────────────┐                     │
│  │ State: Failed       │  │ State: ManualIntervention│                    │
│  │ (Clean completion)  │  │ (Ops team alert)        │                    │
│  │ Saga deleted        │  │ Saga persists           │                    │
│  └─────────────────────┘  └─────────────────────────┘                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
/// <summary>
/// Handle refund failure - requires manual intervention.
/// This is the "Guarded Compensation" pattern.
/// </summary>
public void Handle(
    RefundFailedIntegrationEvent @event,
    ILogger<OrderFulfillmentSaga> logger,
    OrderingMetrics metrics)
{
    State = OrderFulfillmentState.ManualInterventionRequired;
    FailureReason = $"Refund failed: {@event.Reason}. " +
                    $"Payment ID: {PaymentTransactionId}. " +
                    $"Amount: {TotalAmount}. " +
                    "REQUIRES MANUAL RESOLUTION.";

    logger.LogCritical(
        "MANUAL INTERVENTION REQUIRED for Order {OrderId}. " +
        "Refund of {Amount} failed: {Reason}",
        Id, TotalAmount, @event.Reason);

    // Record metric for alerting
    metrics.RecordManualInterventionRequired(Id, TotalAmount);

    // DO NOT call MarkCompleted() - saga persists for ops team
}
```

---

## Error Handling & Dead Letter Queue

### Retry Policy

Wolverine automatically retries failed messages:

```csharp
// Configure retry policy
opts.OnException<DbUpdateConcurrencyException>()
    .RetryWithCooldown(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1));

opts.OnException<HttpRequestException>()
    .RetryWithCooldown(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30));

// After all retries exhausted → Dead Letter Queue
opts.OnException<Exception>()
    .MoveToErrorQueue();
```

### Dead Letter Queue

```sql
-- Query dead letter messages
SELECT
    id,
    message_type,
    body::text,
    exception_message,
    exception_type,
    attempts,
    received_at
FROM wolverine.wolverine_incoming_envelopes
WHERE status = 'dead_letter'
ORDER BY received_at DESC;
```

### Manual Reprocessing

```csharp
// Requeue dead letter messages for reprocessing
public async Task RequeueDeadLetters(
    IMessageStore messageStore,
    string messageType)
{
    var deadLetters = await messageStore.GetDeadLetterMessagesAsync(
        new DeadLetterQuery { MessageType = messageType });

    foreach (var envelope in deadLetters)
    {
        await messageStore.RequeueAsync(envelope.Id);
    }
}
```

---

## Message Serialization

### System.Text.Json Configuration

```csharp
opts.UseSystemTextJsonForSerialization(options =>
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.WriteIndented = false;

    // Pure canonical serialization (legacy resolvers removed in Phase 6)
    // TypeInfoResolver configured at API layer via ConfigureHttpJsonOptions
});
```

### Legacy Type Support (Historical)

Legacy type resolution was removed in Phase 6 after database audits verified no legacy saga state remained. Refer to [PHASE_6_PURGE_COMPLETE.md](./PHASE_6_PURGE_COMPLETE.md) for the purge checklist and rollback guidance.

---

## Idempotency

### Message Deduplication

Wolverine's durable inbox provides message-level idempotency:

```csharp
// MessageIdentity setting ensures unique processing
opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

// Same message ID + same destination = skipped
```

### Application-Level Idempotency

For HTTP endpoints, use the `IdempotencyFilter`:

```csharp
[WolverineHandler]
public static class CreateOrderHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateOrderCommand command,
        IOrderRepository repository,
        IIdempotencyService idempotency)
    {
        // Check if we've already processed this key
        var existing = await idempotency.GetAsync<Guid>(command.IdempotencyKey);
        if (existing.HasValue)
        {
            return Result.Success(existing.Value);
        }

        // Process order...
        var order = Order.Create(...);
        await repository.AddAsync(order);

        // Store result for idempotency
        await idempotency.SetAsync(command.IdempotencyKey, order.Id, TimeSpan.FromHours(24));

        return Result.Success(order.Id);
    }
}
```

---

## Monitoring & Debugging

### Structured Logging

```csharp
logger.LogInformation(
    "Processing {MessageType} for Order {OrderId}. " +
    "Saga State: {SagaState}. Amount: {Amount}",
    nameof(PaymentSucceededIntegrationEvent),
    @event.OrderId,
    State,
    TotalAmount);
```

### Seq Queries

```
// Find all messages for an order
OrderId = "550e8400-e29b-41d4-a716-446655440000"

// Find saga state transitions
MessageType like "%Saga%" and OrderId = "..."

// Find errors in message processing
@Level = 'Error' and SourceContext = 'Wolverine'

// Find dead letter messages
MessageType = "DeadLetterEnvelope"
```

### Metrics

```csharp
public class MessagingMetrics
{
    private readonly Counter<long> _messagesProcessed;
    private readonly Histogram<double> _processingDuration;

    public MessagingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("NetCommerce.Messaging");

        _messagesProcessed = meter.CreateCounter<long>(
            "messages.processed",
            description: "Messages processed by type");

        _processingDuration = meter.CreateHistogram<double>(
            "messages.processing.duration",
            unit: "ms");
    }

    public void RecordProcessed(string messageType)
    {
        _messagesProcessed.Add(1, new KeyValuePair<string, object?>("type", messageType));
    }
}
```

### Admin Endpoints

```csharp
// Get stuck sagas requiring intervention
app.MapGet("/api/admin/sagas/stuck", async (OrderingDbContext db) =>
{
    return await db.Set<OrderFulfillmentSaga>()
        .Where(s => s.State == OrderFulfillmentState.ManualInterventionRequired)
        .ToListAsync();
});

// Requeue dead letter messages
app.MapPost("/api/admin/dlq/requeue", async (
    IMessageStore store,
    RequeueRequest request) =>
{
    await store.RequeueAsync(request.EnvelopeId);
    return Results.Ok();
});
```

---

## Best Practices

### Do's ✅

- Use strongly typed message records
- Return cascading messages from handlers
- Implement compensating transactions for saga failures
- Monitor saga states and dead letter queue
- Use idempotency keys for critical operations
- Log message processing with correlation IDs

### Don'ts ❌

- Don't use request-response across module boundaries
- Don't make handlers dependent on message ordering
- Don't store large payloads in messages
- Don't ignore dead letter messages
- Don't let sagas run indefinitely without timeouts
- Don't skip compensating transaction design

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce Platform Team
