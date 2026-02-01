# NetCommerce Domain Model

> **Domain-Driven Design implementation guide for the NetCommerce platform**

---

## Table of Contents

1. [Domain-Driven Design Overview](#domain-driven-design-overview)
2. [Strategic Design](#strategic-design)
3. [Tactical Design Patterns](#tactical-design-patterns)
4. [Bounded Contexts Deep Dive](#bounded-contexts-deep-dive)
5. [Value Objects](#value-objects)
6. [Domain Events](#domain-events)
7. [Invariants and Business Rules](#invariants-and-business-rules)
8. [Anti-Corruption Layer](#anti-corruption-layer)

---

## Domain-Driven Design Overview

NetCommerce implements Domain-Driven Design (DDD) to manage the complexity of an e-commerce system. The domain model captures business concepts, rules, and processes in code that closely mirrors how domain experts describe the business.

### Core Principles

1. **Ubiquitous Language**: Code uses the same terminology as business stakeholders
2. **Bounded Contexts**: Clear boundaries between different business domains
3. **Rich Domain Models**: Business logic lives in domain objects, not services
4. **Explicit is Better**: Intentions are clear through types and method names

### Why DDD for E-Commerce?

E-commerce has inherent complexity:
- **Pricing rules** (discounts, taxes, currencies)
- **Inventory management** (reservations, stock levels)
- **Order workflows** (status transitions, compensations)
- **Payment processing** (idempotency, reconciliation)

DDD helps manage this complexity by organizing code around business capabilities.

---

## Strategic Design

### Context Map

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         NETCOMMERCE CONTEXT MAP                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│    ┌─────────────┐                              ┌─────────────┐           │
│    │   CATALOG   │         Conformist           │    MEDIA    │           │
│    │   Context   │◀────────────────────────────▶│   Context   │           │
│    │             │      (Product Images)        │             │           │
│    └──────┬──────┘                              └─────────────┘           │
│           │                                                                │
│           │ Published Language (Product Info)                              │
│           │                                                                │
│    ┌──────▼──────┐                              ┌─────────────┐           │
│    │   BASKET    │                              │  SHIPPING   │           │
│    │   Context   │                              │   Context   │           │
│    │             │                              │             │           │
│    └──────┬──────┘                              └──────▲──────┘           │
│           │                                           │                   │
│           │ Customer Facing                           │ ACL               │
│           │                                           │                   │
│    ┌──────▼──────────────────────────────────────────┴─────┐             │
│    │                     ORDERING                           │             │
│    │                      Context                           │             │
│    │                 (Core Domain)                          │             │
│    └──────┬───────────────────────────────────────┬────────┘             │
│           │                                       │                       │
│           │ Partnership                           │ Partnership           │
│           │                                       │                       │
│    ┌──────▼──────┐                         ┌──────▼──────┐               │
│    │  INVENTORY  │                         │  PAYMENTS   │               │
│    │   Context   │                         │   Context   │               │
│    │             │                         │             │               │
│    └─────────────┘                         └──────┬──────┘               │
│                                                   │                       │
│                                                   │ ACL (Stripe)          │
│                                                   │                       │
│                                            ┌──────▼──────┐               │
│                                            │   FINANCE   │               │
│                                            │   Context   │               │
│                                            │             │               │
│                                            └─────────────┘               │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Context Relationships

| Upstream | Downstream | Relationship | Description |
|----------|------------|--------------|-------------|
| Catalog | Basket | Published Language | Basket uses product info |
| Catalog | Ordering | Published Language | Orders snapshot product data |
| Ordering | Inventory | Partnership | Bidirectional reservation flow |
| Ordering | Payments | Partnership | Payment processing integration |
| Payments | Finance | Customer-Supplier | Reconciliation consumes payment data |
| Shipping | Ordering | Conformist | Shipping follows order structure |
| Media | Catalog | Supplier | Provides image URLs |

### Core vs Supporting vs Generic Domains

| Type | Context | Rationale |
|------|---------|-----------|
| **Core** | Ordering | Competitive advantage, complex workflows |
| **Core** | Inventory | Critical for preventing oversells |
| **Supporting** | Catalog | Important but well-understood |
| **Supporting** | Payments | Important but uses external provider |
| **Supporting** | Shipping | Uses external courier APIs |
| **Generic** | Media | Commodity file storage |
| **Generic** | Basket | Standard shopping cart |

---

## Tactical Design Patterns

### Entity Base Class

```csharp
/// <summary>
/// Base class for all domain entities with identity and domain events.
/// </summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity() { }
    protected Entity(TId id) => Id = id;

    public TId Id { get; protected init; } = default!;

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public bool Equals(Entity<TId>? other) =>
        other is not null && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);
}
```

### Aggregate Root Base Class

```csharp
/// <summary>
/// Base class for aggregate roots with optimistic concurrency.
/// Aggregates are transactional boundaries - changes are atomic.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>
    /// Row version for optimistic locking (prevents lost updates).
    /// </summary>
    public uint Version { get; protected set; }

    /// <summary>
    /// Raises a domain event that will be dispatched after SaveChanges.
    /// </summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => AddDomainEvent(domainEvent);
}
```

### Value Object Base Class

```csharp
/// <summary>
/// Base class for immutable value objects.
/// Value objects are defined by their attributes, not identity.
/// </summary>
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
            return false;

        var other = (ValueObject)obj;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) =>
        !Equals(left, right);
}
```

### Strongly Typed IDs

```csharp
/// <summary>
/// Strongly typed ID contract using static abstract members (C# 11+).
/// Prevents primitive obsession and provides compile-time type safety.
/// </summary>
public interface IStronglyTypedId<TId> : IEquatable<TId>, IComparable<TId>, IParsable<TId>
    where TId : struct, IStronglyTypedId<TId>
{
    Guid Value { get; }

    static abstract TId Create(Guid value);
    static TId New() => TId.Create(Guid.NewGuid());
    static TId Empty => TId.Create(Guid.Empty);
}

// Example implementation
public readonly record struct OrderId(Guid Value) : IStronglyTypedId<OrderId>
{
    public static OrderId Create(Guid value) => new(value);

    public static OrderId Parse(string s, IFormatProvider? provider) =>
        new(Guid.Parse(s));

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

    public int CompareTo(OrderId other) => Value.CompareTo(other.Value);
}
```

**Benefits of Strongly Typed IDs:**
```csharp
// Without: Easy to mix up parameters
void ProcessOrder(Guid orderId, Guid customerId, Guid productId) { }
ProcessOrder(productId, orderId, customerId); // Compiles but wrong!

// With: Compile-time safety
void ProcessOrder(OrderId orderId, CustomerId customerId, ProductId productId) { }
ProcessOrder(productId, orderId, customerId); // Compile error!
```

---

## Bounded Contexts Deep Dive

### Catalog Context

**Responsibility:** Product information management and search.

```csharp
/// <summary>
/// Product aggregate - the central entity of the Catalog context.
/// </summary>
public sealed class Product : AggregateRoot<Guid>
{
    private readonly List<ProductAttribute> _attributes = [];
    private readonly List<ProductImage> _images = [];

    public string Name { get; private set; }
    public string Slug { get; private set; }
    public string Description { get; private set; }
    public Money Price { get; private set; }
    public Guid CategoryId { get; private set; }
    public ProductStatus Status { get; private set; }

    public IReadOnlyList<ProductAttribute> Attributes => _attributes.AsReadOnly();
    public IReadOnlyList<ProductImage> Images => _images.AsReadOnly();

    public static Product Create(
        string name,
        string description,
        Money price,
        Guid categoryId)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = Guard.NotNullOrEmpty(name),
            Slug = GenerateSlug(name),
            Description = description,
            Price = price,
            CategoryId = categoryId,
            Status = ProductStatus.Draft
        };

        product.RaiseDomainEvent(new ProductCreatedDomainEvent(product.Id, name, price));
        return product;
    }

    public Result UpdatePrice(Money newPrice)
    {
        if (newPrice.Amount <= 0)
            return Result.Failure(Error.Validation("Price must be positive"));

        var oldPrice = Price;
        Price = newPrice;

        RaiseDomainEvent(new ProductPriceChangedDomainEvent(Id, oldPrice, newPrice));
        return Result.Success();
    }

    public Result Publish()
    {
        if (Status == ProductStatus.Published)
            return Result.Failure(Error.Conflict("Product is already published"));

        if (!_images.Any())
            return Result.Failure(Error.Validation("Product must have at least one image"));

        Status = ProductStatus.Published;
        RaiseDomainEvent(new ProductPublishedDomainEvent(Id));
        return Result.Success();
    }
}

public enum ProductStatus { Draft, Published, Archived }
```

### Ordering Context

**Responsibility:** Order lifecycle management and fulfillment orchestration.

```csharp
/// <summary>
/// Order aggregate - implements state machine pattern with price snapshotting.
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderItem> _items = [];

    public string OrderNumber { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public Money TotalAmount { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; }
    public string IdempotencyKey { get; private set; }

    // Audit timestamps
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();
    public bool IsInGracePeriod => Status == OrderStatus.Submitted;

    public static Order Create(
        Guid customerId,
        ShippingAddress shippingAddress,
        string idempotencyKey)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = GenerateOrderNumber(),
            CustomerId = customerId,
            Status = OrderStatus.Submitted,
            ShippingAddress = shippingAddress,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow,
            TotalAmount = Money.Zero()
        };

        order.RaiseDomainEvent(new OrderSubmittedDomainEvent(
            order.Id, order.OrderNumber, customerId));

        return order;
    }

    /// <summary>
    /// Adds an item with PRICE SNAPSHOTTING - captures current price.
    /// Future price changes don't affect this order.
    /// </summary>
    public void AddItem(Guid productId, string productName, Money unitPrice, int quantity)
    {
        var item = OrderItem.Create(productId, productName, unitPrice, quantity);
        _items.Add(item);
        RecalculateTotal();
    }

    /// <summary>
    /// State transition: Submitted → Paid
    /// </summary>
    public Result ConfirmPayment(string transactionId)
    {
        if (Status != OrderStatus.Submitted)
            return Result.Failure(Error.Conflict(
                $"Cannot confirm payment for order in {Status} status"));

        Status = OrderStatus.Paid;
        PaymentTransactionId = transactionId;
        PaidAt = DateTime.UtcNow;

        RaiseDomainEvent(new OrderPaidDomainEvent(Id, transactionId, TotalAmount));
        return Result.Success();
    }

    /// <summary>
    /// State transition: Submitted → Cancelled (within grace period)
    /// </summary>
    public Result Cancel(string reason, bool isAdmin = false)
    {
        if (Status == OrderStatus.Cancelled)
            return Result.Failure(Error.Conflict("Order is already cancelled"));

        if (Status != OrderStatus.Submitted && !isAdmin)
            return Result.Failure(Error.Conflict(
                "Only submitted orders can be cancelled by customers"));

        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = DateTime.UtcNow;

        RaiseDomainEvent(new OrderCancelledDomainEvent(Id, reason, previousStatus));
        return Result.Success();
    }

    private void RecalculateTotal()
    {
        TotalAmount = _items.Aggregate(
            Money.Zero(),
            (total, item) => total.Add(item.TotalPrice));
    }
}

public enum OrderStatus
{
    Submitted,      // Initial state, within grace period
    Paid,           // Payment confirmed
    Processing,     // Being prepared
    Shipped,        // Dispatched to customer
    Delivered,      // Successfully delivered
    Cancelled,      // Cancelled by customer or system
    Refunded        // Payment refunded
}
```

### Inventory Context

**Responsibility:** Stock management with reservation pattern.

```csharp
/// <summary>
/// Stock aggregate - manages inventory with soft reservation pattern.
/// </summary>
public sealed class Stock : AggregateRoot<Guid>
{
    private readonly List<StockReservation> _reservations = [];

    public Guid ProductId { get; private set; }
    public int TotalQuantity { get; private set; }
    public int ReorderPoint { get; private set; }
    public DateTime? LastRestockedAt { get; private set; }

    /// <summary>
    /// Available = Total - Reserved (pending reservations)
    /// </summary>
    public int AvailableQuantity => TotalQuantity - _reservations
        .Where(r => r.Status == ReservationStatus.Pending)
        .Sum(r => r.Quantity);

    public int ReservedQuantity => _reservations
        .Where(r => r.Status == ReservationStatus.Pending)
        .Sum(r => r.Quantity);

    public bool IsLowStock => AvailableQuantity <= ReorderPoint;

    public IReadOnlyList<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>
    /// SOFT RESERVATION: Holds stock without deducting.
    /// Used during checkout before payment confirmation.
    /// </summary>
    public Result<Guid> Reserve(Guid orderId, int quantity, TimeSpan expiry)
    {
        if (quantity <= 0)
            return Result.Failure<Guid>(Error.Validation("Quantity must be positive"));

        if (AvailableQuantity < quantity)
            return Result.Failure<Guid>(Error.Conflict(
                $"Insufficient stock. Available: {AvailableQuantity}, Requested: {quantity}"));

        var reservation = StockReservation.Create(orderId, quantity, expiry);
        _reservations.Add(reservation);

        RaiseDomainEvent(new StockReservedDomainEvent(
            ProductId, orderId, reservation.Id, quantity));

        if (IsLowStock)
            RaiseDomainEvent(new LowStockAlertDomainEvent(ProductId, AvailableQuantity));

        return Result.Success(reservation.Id);
    }

    /// <summary>
    /// CONFIRM RESERVATION: Converts soft reservation to hard deduction.
    /// Called after successful payment.
    /// </summary>
    public Result ConfirmReservation(Guid reservationId)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);
        if (reservation is null)
            return Result.Failure(Error.NotFound("Reservation", reservationId));

        if (reservation.Status != ReservationStatus.Pending)
            return Result.Failure(Error.Conflict(
                $"Reservation is {reservation.Status}, cannot confirm"));

        reservation.Confirm();
        TotalQuantity -= reservation.Quantity; // HARD DEDUCTION

        RaiseDomainEvent(new StockDeductedDomainEvent(
            ProductId, reservation.OrderId, reservation.Quantity));

        return Result.Success();
    }

    /// <summary>
    /// RELEASE RESERVATION: Returns reserved stock to available pool.
    /// Called on payment failure, order cancellation, or expiry.
    /// </summary>
    public Result ReleaseReservation(Guid reservationId)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);
        if (reservation is null)
            return Result.Failure(Error.NotFound("Reservation", reservationId));

        if (reservation.Status != ReservationStatus.Pending)
            return Result.Failure(Error.Conflict("Reservation already processed"));

        reservation.Release();

        RaiseDomainEvent(new StockReleasedDomainEvent(
            ProductId, reservation.OrderId, reservation.Quantity));

        return Result.Success();
    }

    public void Restock(int quantity, string reason)
    {
        TotalQuantity += quantity;
        LastRestockedAt = DateTime.UtcNow;

        RaiseDomainEvent(new StockRestockedDomainEvent(ProductId, quantity, reason));
    }
}

public sealed class StockReservation : Entity<Guid>
{
    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsExpired => Status == ReservationStatus.Pending
        && DateTime.UtcNow > ExpiresAt;

    public void Confirm() => Status = ReservationStatus.Confirmed;
    public void Release() => Status = ReservationStatus.Released;
    public void Expire() => Status = ReservationStatus.Expired;
}

public enum ReservationStatus { Pending, Confirmed, Released, Expired }
```

### Payments Context

**Responsibility:** Payment processing and transaction management.

```csharp
/// <summary>
/// PaymentTransaction aggregate - tracks payment lifecycle.
/// </summary>
public sealed class PaymentTransaction : AggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }
    public Money Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? ExternalTransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public string IdempotencyKey { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public DateTime? RefundedAt { get; private set; }

    public static PaymentTransaction Create(
        Guid orderId,
        Money amount,
        string idempotencyKey)
    {
        return new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = amount,
            Status = PaymentStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow
        };
    }

    public Result MarkAsSucceeded(string externalTransactionId)
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(Error.Conflict($"Cannot succeed from {Status}"));

        Status = PaymentStatus.Succeeded;
        ExternalTransactionId = externalTransactionId;
        ProcessedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentSucceededDomainEvent(
            Id, OrderId, ExternalTransactionId, Amount));

        return Result.Success();
    }

    public Result MarkAsFailed(string reason)
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(Error.Conflict($"Cannot fail from {Status}"));

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        ProcessedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentFailedDomainEvent(Id, OrderId, reason));

        return Result.Success();
    }

    public Result Refund()
    {
        if (Status != PaymentStatus.Succeeded)
            return Result.Failure(Error.Conflict("Can only refund succeeded payments"));

        Status = PaymentStatus.Refunded;
        RefundedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentRefundedDomainEvent(
            Id, OrderId, ExternalTransactionId!, Amount));

        return Result.Success();
    }
}

public enum PaymentStatus { Pending, Succeeded, Failed, Refunded }
```

---

## Value Objects

### Money

```csharp
/// <summary>
/// Monetary value with currency support.
/// NetCommerce default currency is GEL (Georgian Lari).
/// </summary>
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    [JsonConstructor]
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency = "GEL")
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative");
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required");

        return new Money(Math.Round(amount, 2), currency.ToUpperInvariant());
    }

    public static Money Zero(string currency = "GEL") => new(0, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal multiplier) =>
        new(Math.Round(Amount * multiplier, 2), Currency);

    /// <summary>
    /// Converts to subunits (cents) for payment processors.
    /// </summary>
    public long ToSubunits() =>
        Convert.ToInt64(Math.Round(Amount * 100, 0, MidpointRounding.AwayFromZero));

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException(
                $"Currency mismatch: {Currency} vs {other.Currency}");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
```

### Address Value Objects

```csharp
/// <summary>
/// Shipping address with validation.
/// </summary>
public sealed class ShippingAddress : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string PostalCode { get; }
    public string Country { get; }
    public string? ApartmentNumber { get; }
    public string? DeliveryInstructions { get; }

    private ShippingAddress(
        string street, string city, string state,
        string postalCode, string country,
        string? apartmentNumber, string? deliveryInstructions)
    {
        Street = street;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        ApartmentNumber = apartmentNumber;
        DeliveryInstructions = deliveryInstructions;
    }

    public static Result<ShippingAddress> Create(
        string street, string city, string state,
        string postalCode, string country,
        string? apartmentNumber = null,
        string? deliveryInstructions = null)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(street))
            errors.Add("Street is required");
        if (string.IsNullOrWhiteSpace(city))
            errors.Add("City is required");
        if (string.IsNullOrWhiteSpace(country))
            errors.Add("Country is required");

        if (errors.Any())
            return Result.Failure<ShippingAddress>(
                Error.Validation(string.Join("; ", errors)));

        return new ShippingAddress(
            street, city, state, postalCode, country,
            apartmentNumber, deliveryInstructions);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
        yield return ApartmentNumber;
    }
}
```

---

## Domain Events

### Event Categories

| Category | Purpose | Example |
|----------|---------|---------|
| **Domain Events** | Internal to module | `OrderSubmittedDomainEvent` |
| **Integration Events** | Cross-module | `OrderSubmittedIntegrationEvent` |

### Domain Event Base

```csharp
/// <summary>
/// Marker interface for domain events.
/// Domain events are internal to a bounded context.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

### Integration Event Base

```csharp
/// <summary>
/// Base class for integration events that cross bounded context boundaries.
/// Published via Wolverine transactional outbox.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string EventType => GetType().Name;
}
```

### Event Examples

```csharp
// Domain event (stays within Ordering module)
public sealed record OrderSubmittedDomainEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId) : DomainEvent;

// Integration event (crosses to Inventory module)
public sealed record OrderSubmittedIntegrationEvent(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    List<OrderItemDto> Items,
    Money TotalAmount) : IntegrationEvent;

// Domain event handler converts to integration event
public static class OrderSubmittedHandler
{
    public static OrderSubmittedIntegrationEvent Handle(
        OrderSubmittedDomainEvent @event,
        IOrderRepository repository)
    {
        var order = repository.GetById(@event.OrderId);

        return new OrderSubmittedIntegrationEvent(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.Items.Select(i => i.ToDto()).ToList(),
            order.TotalAmount);
    }
}
```

---

## Invariants and Business Rules

### Order Invariants

```csharp
public sealed class Order : AggregateRoot<Guid>
{
    // INVARIANT: Order must have at least one item
    public Result AddItem(...)
    {
        // ... add item logic
        if (!_items.Any())
            return Result.Failure(Error.Validation("Order must have items"));
    }

    // INVARIANT: Can only cancel within grace period (unless admin)
    public Result Cancel(string reason, bool isAdmin)
    {
        if (Status != OrderStatus.Submitted && !isAdmin)
            return Result.Failure(Error.Conflict(
                "Only orders in grace period can be cancelled"));
    }

    // INVARIANT: State transitions must follow valid paths
    public Result ConfirmPayment(...)
    {
        if (Status != OrderStatus.Submitted)
            return Result.Failure(Error.Conflict(
                $"Invalid state transition: {Status} → Paid"));
    }
}
```

### Stock Invariants

```csharp
public sealed class Stock : AggregateRoot<Guid>
{
    // INVARIANT: Cannot reserve more than available
    public Result<Guid> Reserve(Guid orderId, int quantity, TimeSpan expiry)
    {
        if (AvailableQuantity < quantity)
            return Result.Failure<Guid>(Error.Conflict(
                $"Insufficient stock: {AvailableQuantity} < {quantity}"));
    }

    // INVARIANT: Cannot go negative
    public Result ConfirmReservation(Guid reservationId)
    {
        if (TotalQuantity - reservation.Quantity < 0)
            return Result.Failure(Error.Conflict("Would result in negative stock"));
    }
}
```

---

## Anti-Corruption Layer

### External Payment Gateway ACL

```csharp
/// <summary>
/// Anti-Corruption Layer: Translates Stripe concepts to domain concepts.
/// Protects domain from external API changes.
/// </summary>
public sealed class StripePaymentGatewayAdapter : IPaymentGateway
{
    private readonly StripeClient _stripeClient;

    public async Task<PaymentResult> ProcessPaymentAsync(
        Money amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Translate domain Money to Stripe's representation
        var stripeOptions = new PaymentIntentCreateOptions
        {
            Amount = amount.ToSubunits(), // Convert to cents
            Currency = amount.Currency.ToLowerInvariant(),
            IdempotencyKey = idempotencyKey
        };

        try
        {
            var intent = await _stripeClient.PaymentIntents.CreateAsync(
                stripeOptions, cancellationToken: cancellationToken);

            // Translate Stripe response to domain result
            return intent.Status switch
            {
                "succeeded" => PaymentResult.Success(intent.Id),
                "requires_action" => PaymentResult.RequiresAction(intent.ClientSecret),
                _ => PaymentResult.Failed($"Unexpected status: {intent.Status}")
            };
        }
        catch (StripeException ex)
        {
            // Translate Stripe errors to domain errors
            return PaymentResult.Failed(MapStripeError(ex));
        }
    }

    private static string MapStripeError(StripeException ex) => ex.StripeError.Code switch
    {
        "card_declined" => "Payment was declined",
        "insufficient_funds" => "Insufficient funds",
        "expired_card" => "Card has expired",
        _ => "Payment processing failed"
    };
}
```

### Courier Service ACL

```csharp
/// <summary>
/// Anti-Corruption Layer: Abstracts courier-specific APIs.
/// </summary>
public interface ICourierAdapter
{
    Task<CourierLabelResult> CreateLabelAsync(ShipmentRequest request);
    Task<TrackingStatus> GetTrackingStatusAsync(string trackingNumber);
    Task CancelLabelAsync(string trackingNumber);
}

// Domain uses courier-agnostic types
public sealed record ShipmentRequest(
    ShippingAddress Destination,
    decimal WeightKg,
    PackageDimensions Dimensions);

public sealed record CourierLabelResult(
    string TrackingNumber,
    string LabelUrl,
    Money ShippingCost,
    DateTime EstimatedDelivery);
```

---

## Best Practices Summary

### Do's ✅

- Use Strongly Typed IDs for all entity identifiers
- Implement rich domain models with behavior
- Validate invariants within aggregate boundaries
- Use Value Objects for concepts without identity
- Raise domain events for significant state changes
- Keep aggregates small and focused

### Don'ts ❌

- Don't expose setters on aggregate properties
- Don't allow direct collection manipulation
- Don't perform I/O in domain objects
- Don't reference other aggregates by object reference
- Don't let transactions span multiple aggregates
- Don't use anemic domain models (behavior-less data bags)

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce Domain Team
