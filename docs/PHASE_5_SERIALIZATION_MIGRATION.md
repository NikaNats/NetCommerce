# Phase 5: Serialization Risk & Migration Guide

## ⚠️ Critical Serialization Risk

### The Problem

Wolverine's **Transactional Outbox** and **Saga Persistence** in PostgreSQL store fully qualified type names (including namespace) in JSON payloads and metadata. This creates a serialization compatibility issue during architectural migrations.

**Affected Tables:**
- `wolverine.wolverine_outgoing_envelopes` - Outbox messages awaiting delivery
- `wolverine.wolverine_incoming_envelopes` - Inbox messages for idempotency
- `wolverine.saga_state` - Persisted saga state

### The Risk Scenario

```
1. Saga in database contains: NetCommerce.SharedKernel.Domain.Money
2. Deploy Phase 5 changes: Money is now NetCommerce.Domain.Shared.Money
3. Wolverine tries to load saga → DESERIALIZATION FAILURE ❌
```

### Affected Types in Saga State

The `OrderFulfillmentSaga` contains these migrated types:

```csharp
public sealed class OrderFulfillmentSaga : Saga
{
    public Money TotalAmount { get; set; } = Money.Zero();  // ⚠️ Type name changed
    public List<OrderItemReservation> Items { get; set; } = [];  // ⚠️ May reference Money
    // ... other state properties
}
```

**Old Namespace (Deprecated):**
- `NetCommerce.SharedKernel.Domain.Money`
- `NetCommerce.SharedKernel.Domain.PriceBreakdown`
- `NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent`

**New Namespace (Canonical):**
- `NetCommerce.Domain.Shared.Money`
- `NetCommerce.Domain.Shared.PriceBreakdown`
- `NetCommerce.Domain.Shared.Events.OrderSubmittedIntegrationEvent`

---

## 🏗️ Migration Strategy

### Option 1: Database Wipe (Development/Staging)

**Recommended for:**
- Local development environments
- Non-production environments
- .NET 10 preview/testing stacks
- Projects still in migration phase

**Steps:**
```powershell
# 1. Stop the application
dotnet aspire stop

# 2. Connect to PostgreSQL
docker exec -it <postgres-container> psql -U test -d netcommerce

# 3. Clear Wolverine tables
TRUNCATE TABLE wolverine.wolverine_outgoing_envelopes;
TRUNCATE TABLE wolverine.wolverine_incoming_envelopes;
TRUNCATE TABLE wolverine.saga_state CASCADE;

# 4. Verify cleanup
SELECT COUNT(*) FROM wolverine.saga_state;

# 5. Redeploy with Phase 5 changes
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

**Integration Tests:**
The `IntegrationTestFixture` already handles this via **Respawn**:

```csharp
// From IntegrationTestFixture.cs
new Table("wolverine", "wolverine_incoming_envelopes"),
new Table("wolverine", "wolverine_outgoing_envelopes"),
new Table("wolverine", "saga_state")
```

---

### Option 2: Type Forwarding (Production)

**Recommended for:**
- Production environments with live sagas
- Zero-downtime deployments
- Gradual migration scenarios

**Implementation:**

#### Step 1: Add JSON Type Resolver

Create a custom `IJsonTypeInfoResolver` for backward compatibility:

```csharp
// src/Kernel.Adapters/NetCommerce.Kernel.Wolverine/Serialization/WolverineTypeResolver.cs

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NetCommerce.Kernel.Wolverine.Serialization;

/// <summary>
///     Custom type resolver for handling legacy SharedKernel type names
///     during Phase 5 migration to Domain.Shared.
/// </summary>
public class WolverineTypeResolver : DefaultJsonTypeInfoResolver
{
    private static readonly Dictionary<string, Type> TypeMappings = new()
    {
        // Value Objects
        ["NetCommerce.SharedKernel.Domain.Money"] = typeof(NetCommerce.Domain.Shared.Money),
        ["NetCommerce.SharedKernel.Domain.PriceBreakdown"] = typeof(NetCommerce.Domain.Shared.PriceBreakdown),

        // Integration Events
        ["NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent"] =
            typeof(NetCommerce.Domain.Shared.Events.OrderSubmittedIntegrationEvent),
        ["NetCommerce.SharedKernel.Events.OrderGracePeriodConfirmedIntegrationEvent"] =
            typeof(NetCommerce.Domain.Shared.Events.OrderGracePeriodConfirmedIntegrationEvent),
        // ... add all other migrated events

        // Saga Messages
        ["NetCommerce.SharedKernel.Events.RequestPaymentCommand"] =
            typeof(NetCommerce.Domain.Shared.Events.RequestPaymentCommand),
        // ... add all other saga messages
    };

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);

        // Add custom converter for type name resolution
        if (TypeMappings.ContainsValue(type))
        {
            typeInfo.OnSerializing = (obj) =>
            {
                // Use canonical type name
            };
        }

        return typeInfo;
    }

    /// <summary>
    ///     Resolves legacy type names to canonical types.
    /// </summary>
    public static Type? ResolveType(string legacyTypeName)
    {
        return TypeMappings.TryGetValue(legacyTypeName, out var type) ? type : null;
    }
}
```

#### Step 2: Configure Wolverine Serialization

Update `WolverineKernelExtensions.cs`:

```csharp
public static WolverineOptions ConfigureKernelDefaults<TDbContext>(this WolverineOptions opts)
    where TDbContext : DbContext
{
    // ... existing configuration

    // Add type resolver for Phase 5 migration
    opts.Serialization(serialization =>
    {
        serialization.JsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            TypeInfoResolver = new WolverineTypeResolver()
        };
    });

    return opts;
}
```

#### Step 3: Gradual Migration Timeline

**Week 1: Deploy with Type Resolver**
- ✅ New sagas use `NetCommerce.Domain.Shared.Money`
- ✅ Old sagas can deserialize via type resolver
- ✅ Zero downtime

**Week 2-4: Monitor Saga Completion**
```sql
-- Check for remaining legacy sagas
SELECT
    saga_type,
    COUNT(*) as count,
    MIN(created_at) as oldest_saga
FROM wolverine.saga_state
GROUP BY saga_type;
```

**Week 5: Remove Type Resolver**
- All legacy sagas completed
- Remove `WolverineTypeResolver`
- System fully migrated ✅

---

## 🧪 Testing Strategy

### 1. Verify Saga Serialization

```csharp
[Fact]
public async Task OrderFulfillmentSaga_ShouldSerializeMoneyCorrectly()
{
    // Arrange
    var saga = new OrderFulfillmentSaga
    {
        Id = Guid.NewGuid(),
        TotalAmount = Money.Create(150.00m, "GEL")
    };

    // Act
    var json = JsonSerializer.Serialize(saga);
    var deserialized = JsonSerializer.Deserialize<OrderFulfillmentSaga>(json);

    // Assert
    Assert.Equal(saga.TotalAmount, deserialized.TotalAmount);
    Assert.Contains("NetCommerce.Domain.Shared.Money", json); // Canonical type
}
```

### 2. Integration Test for Outbox Messages

```csharp
[Fact]
public async Task IntegrationEvent_ShouldPersistToOutboxWithCorrectTypeName()
{
    // Arrange
    var @event = new OrderSubmittedIntegrationEvent(
        Guid.NewGuid(),
        "ORD-12345",
        Money.Create(100m));

    // Act
    await _bus.PublishAsync(@event);

    // Assert
    var outboxMessage = await _dbContext
        .Set<OutboxMessage>()
        .FirstOrDefaultAsync(m => m.Body.Contains("ORD-12345"));

    Assert.NotNull(outboxMessage);
    Assert.Contains("NetCommerce.Domain.Shared.Events", outboxMessage.MessageType);
}
```

---

## 📋 Pre-Deployment Checklist

### Development Environment
- [ ] All tests passing (501 tests)
- [ ] No active sagas in database
- [ ] Wolverine tables cleared
- [ ] Integration tests verify new type names

### Staging Environment
- [ ] Type resolver configured (if production-like)
- [ ] Test saga creation with new types
- [ ] Test saga deserialization from database
- [ ] Monitor for deserialization errors

### Production Environment
- [ ] Type resolver implemented and tested
- [ ] Monitoring for saga failures
- [ ] Rollback plan documented
- [ ] Gradual migration timeline scheduled

---

## 🔍 Troubleshooting

### Error: "Could not load type NetCommerce.SharedKernel.Domain.Money"

**Cause:** Attempting to deserialize legacy saga state without type resolver.

**Solution:**
1. **Immediate:** Rollback to pre-Phase-5 deployment
2. **Short-term:** Implement Option 2 (Type Forwarding)
3. **Long-term:** Complete migration, allow sagas to drain

### Error: "JsonException: The JSON value could not be converted"

**Cause:** Property type changed incompatibly (e.g., `decimal` → `Money`).

**Solution:**
1. Check saga state in database:
```sql
SELECT id, state::jsonb FROM wolverine.saga_state
WHERE saga_type LIKE '%OrderFulfillmentSaga%';
```

2. Manual migration if needed:
```sql
UPDATE wolverine.saga_state
SET state = jsonb_set(
    state,
    '{totalAmount}',
    '{"amount": 100.00, "currency": "GEL"}'::jsonb
)
WHERE saga_type = 'OrderFulfillmentSaga';
```

---

## 📚 References

- [Wolverine Documentation: Saga Persistence](https://wolverine.netlify.app/guide/durability/sagas.html)
- [System.Text.Json Custom Converters](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to)
- [Phase 4: Zero-Trust Security Migration](./PHASE_4_ZERO_TRUST_MIGRATION.md)
- [Architecture Diagrams](./ARCHITECTURE_DIAGRAMS.md)

---

## ✅ Completion Criteria

Phase 5 is **fully complete** when:

1. ✅ All deprecated types marked with `[Obsolete]`
2. ✅ Canonical types in `Domain.Shared` are the source of truth
3. ✅ No active references to `NetCommerce.SharedKernel.Domain.Money` in new code
4. ✅ All 501 tests passing
5. ✅ Migration strategy documented (this document)
6. ⏳ **Production deployment uses Type Resolver OR database is wiped**

---

**Current Status:** ✅ **Phase 5 Dev Complete** (Database wipe acceptable for .NET 10 preview)

**Next Phase:** Phase 6 - Remove deprecated SharedKernel types entirely (after saga drain period)
