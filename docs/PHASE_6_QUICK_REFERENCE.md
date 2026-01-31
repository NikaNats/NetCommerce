# Phase 6: Quick Reference - SharedKernel to Kernel.* Migration

## Namespace Mapping

### Value Objects & Domain Types
| ❌ Old (Deleted) | ✅ New (Canonical) |
|---|---|
| `NetCommerce.SharedKernel.Domain.Money` | `NetCommerce.Domain.Shared.Money` |
| `NetCommerce.SharedKernel.Domain.PriceBreakdown` | `NetCommerce.Domain.Shared.PriceBreakdown` |

### Integration Events
| ❌ Old (Deleted) | ✅ New (Canonical) |
|---|---|
| `NetCommerce.SharedKernel.Events.OrderSubmittedIntegrationEvent` | `NetCommerce.Domain.Shared.Events.OrderSubmittedIntegrationEvent` |
| `NetCommerce.SharedKernel.Events.*` | `NetCommerce.Domain.Shared.Events.*` |

### Infrastructure Extensions
| ❌ Old (Deleted) | ✅ New (Canonical) | File Location |
|---|---|---|
| `SharedKernel.Infrastructure.Kestrel.AddEnterpriseWebHost()` | `NetCommerce.Kernel.AspNetCore.KestrelExtensions.AddEnterpriseWebHost()` | `src/Kernel.Adapters/NetCommerce.Kernel.AspNetCore/KestrelExtensions.cs` |
| `SharedKernel.Infrastructure.Kestrel.UseEnterpriseWebHost()` | `NetCommerce.Kernel.AspNetCore.KestrelExtensions.UseEnterpriseWebHost()` | `src/Kernel.Adapters/NetCommerce.Kernel.AspNetCore/KestrelExtensions.cs` |
| `SharedKernel.Infrastructure.Versioning.AddVersioning()` | `NetCommerce.Kernel.AspNetCore.VersioningExtensions.AddVersioning()` | `src/Kernel.Adapters/NetCommerce.Kernel.AspNetCore/VersioningExtensions.cs` |
| `SharedKernel.Infrastructure.Versioning.GetDefaultApiVersionSet()` | `NetCommerce.Kernel.AspNetCore.VersioningExtensions.GetDefaultApiVersionSet()` | `src/Kernel.Adapters/NetCommerce.Kernel.AspNetCore/VersioningExtensions.cs` |
| `SharedKernel.Infrastructure.Wolverine.UseWolverineMessaging()` | `NetCommerce.Kernel.Wolverine.WolverineMessagingExtensions.UseWolverineMessaging()` | `src/Kernel.Adapters/NetCommerce.Kernel.Wolverine/WolverineMessagingExtensions.cs` |

### Security/Authentication
| ❌ Old (Deleted) | ✅ New (Canonical) |
|---|---|
| `KeycloakRolesClaimsTransformation` | `OidcRoleClaimsTransformation` (NetCommerce.Kernel.Security.Authentication) |

### Encryption/Compliance
| ❌ Old (Deleted) | ✅ New (Canonical) |
|---|---|
| `SharedKernel.Infrastructure.Encryption.IEncryptionService` | `NetCommerce.Kernel.Compliance.Encryption.IEncryptionService` |
| `SharedKernel.Infrastructure.Encryption.IBlindIndexSaltProvider` | `NetCommerce.Kernel.Compliance.Encryption.IBlindIndexSaltProvider` |

## Common Migration Patterns

### Pattern 1: Update Using Statements

**Before:**
```csharp
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Events;
using NetCommerce.SharedKernel.Infrastructure.Kestrel;
```

**After:**
```csharp
using NetCommerce.Domain.Shared;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Kernel.AspNetCore;
```

### Pattern 2: Extension Method Calls (No Change Required)

**Program.cs** - No changes needed (extension methods migrated internally):
```csharp
// Kestrel configuration (now in Kernel.AspNetCore)
builder.AddEnterpriseWebHost();
app.UseEnterpriseWebHost();

// API Versioning (now in Kernel.AspNetCore)
builder.Services.AddVersioning();
var versionSet = app.GetDefaultApiVersionSet();

// Wolverine messaging (now in Kernel.Wolverine)
builder.Host.UseWolverineMessaging(builder.Configuration, /* handler types */);
```

### Pattern 3: Integration Event Publishing

**Before:**
```csharp
using NetCommerce.SharedKernel.Events;

var @event = new OrderSubmittedIntegrationEvent(orderId, orderNumber, totalAmount);
```

**After:**
```csharp
using NetCommerce.Domain.Shared.Events;

var @event = new OrderSubmittedIntegrationEvent(orderId, orderNumber, totalAmount);
```

### Pattern 4: Money Value Object

**No Change Required** - Type location changed but API remains identical:
```csharp
using NetCommerce.Domain.Shared;

var price = Money.Create(100m);              // 100 GEL (default)
var usdPrice = Money.Create(50m, "USD");     // 50 USD
var total = price + usdPrice;                // 150 GEL (auto-converts)
```

## Migration Checklist for New Code

When writing new code, ensure:

1. ✅ **Never** import `using NetCommerce.SharedKernel.*` (namespace deleted)
2. ✅ Use `NetCommerce.Domain.Shared` for `Money`, `PriceBreakdown`, etc.
3. ✅ Use `NetCommerce.Domain.Shared.Events.*` for integration events
4. ✅ Use `NetCommerce.Kernel.AspNetCore` for web host extensions
5. ✅ Use `NetCommerce.Kernel.Wolverine` for messaging configuration
6. ✅ Use `NetCommerce.Kernel.Compliance.Encryption` for encryption services
7. ✅ Use `OidcRoleClaimsTransformation` (not `KeycloakRolesClaimsTransformation`)

## Architecture Test (Future)

To prevent regression, add architecture test:

```csharp
// tests/NetCommerce.Architecture.Tests/SharedKernelBanTests.cs
[Fact]
public void SharedKernel_ShouldNotBeReferenced()
{
    var result = Types.InCurrentDomain()
        .That().ResideInNamespace("NetCommerce")
        .Should().NotHaveDependencyOn("NetCommerce.SharedKernel")
        .GetResult();

    Assert.True(result.IsSuccessful);
}
```

## Deleted Files Summary

### Directories
- `src/Shared/SharedKernel/` (entire directory tree)
- `src/Shared/SharedKernel.Infrastructure/` (entire directory tree)

### Project References
- Removed from `NetCommerce.slnx` solution file
- Removed from `tests/NetCommerce.Domain.Tests/NetCommerce.Domain.Tests.csproj`
- Removed from `src/Ordering/Ordering.Infrastructure/Ordering.Infrastructure.csproj`
- Removed from `src/Api/NetCommerce.Api.csproj`

## When Merging Code from Old Branches

If you merge code from pre-Phase-6 branches:

1. **Build will fail** with CS0234 errors (namespace not found)
2. **Search & Replace:**
   - Find: `using NetCommerce.SharedKernel.Domain;`
   - Replace: `using NetCommerce.Domain.Shared;`

   - Find: `using NetCommerce.SharedKernel.Events`
   - Replace: `using NetCommerce.Domain.Shared.Events`

3. **Test compilation** - Extension methods should work without changes
4. **Run tests** to verify behavior unchanged

## Related Documentation

- [Phase 5: Serialization Risk & Migration Guide](./PHASE_5_SERIALIZATION_MIGRATION.md) - Background on [Obsolete] attribute strategy
- [Phase 6 Complete Summary](./PHASE_6_COMPLETE.md) - Detailed completion report
- [Architecture Diagrams](./ARCHITECTURE_DIAGRAMS.md) - System architecture overview

---

**Last Updated:** 2025-01-25
**Migration Status:** ✅ Complete
