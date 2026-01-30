# Phase 5 Quick Reference Card

## 🎯 What Changed?

Value objects and integration events have been consolidated from `SharedKernel` to the canonical `Domain.Shared` namespace.

## 📝 Developer Checklist

### When Writing New Code

❌ **DON'T:**
```csharp
using NetCommerce.SharedKernel.Domain;  // DEPRECATED
using NetCommerce.SharedKernel.Events;  // DEPRECATED

var money = new Money(100m, "GEL");  // Compiler warning CS0618
```

✅ **DO:**
```csharp
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;

var money = Money.Create(100m);  // Uses canonical type
```

---

## 🔄 Migration Map

| Legacy (Deprecated) | Canonical (Use This) |
|:---|:---|
| `NetCommerce.SharedKernel.Domain.Money` | `NetCommerce.Domain.Shared.Money` |
| `NetCommerce.SharedKernel.Domain.PriceBreakdown` | `NetCommerce.Domain.Shared.PriceBreakdown` |
| `NetCommerce.SharedKernel.Events.*IntegrationEvent` | `NetCommerce.Domain.Shared.Events.*IntegrationEvent` |
| `NetCommerce.SharedKernel.Events.*Command` | `NetCommerce.Domain.Shared.Events.*Command` |

---

## 🚨 Wolverine Saga Warning

**If you're deploying to a database with existing saga state:**

⚠️ Read [docs/PHASE_5_SERIALIZATION_MIGRATION.md](../docs/PHASE_5_SERIALIZATION_MIGRATION.md) first!

**Quick Fix for Dev:**
```sql
-- Clear Wolverine tables (dev only!)
TRUNCATE TABLE wolverine.wolverine_outgoing_envelopes;
TRUNCATE TABLE wolverine.wolverine_incoming_envelopes;
TRUNCATE TABLE wolverine.saga_state CASCADE;
```

---

## ✅ Before Committing

Run the full test suite:
```powershell
dotnet test NetCommerce.slnx -v minimal --nologo
```

**Expected:** 501 tests (486 passed, 15 skipped load tests)

---

## 📚 Full Documentation

- [Phase 5 Serialization Migration](../docs/PHASE_5_SERIALIZATION_MIGRATION.md)
- [Architecture Diagrams](../docs/ARCHITECTURE_DIAGRAMS.md)
- [NetCommerce Copilot Instructions](../copilot-instructions.md)
