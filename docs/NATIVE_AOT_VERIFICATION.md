# Native AOT Deep Verification Protocol

## Overview

Native AOT compilation **succeeds even with fatal runtime bugs**. Unit tests run in JIT mode and provide false confidence. The only valid test is executing the actual native binary.

This protocol provides 5 mandatory checkpoints to verify your Native AOT build is production-ready.

## ⚠️ Critical Understanding

- **JIT Tests Pass ≠ AOT Works**: Your unit tests use reflection and dynamic code generation
- **Build Succeeds ≠ Runtime Works**: The ILC compiler generates a binary even with missing metadata
- **Local Dev Works ≠ Container Works**: Aspire uses JIT; Docker uses the native binary

**Bottom Line:** You MUST run these checkpoints against the actual Docker container.

---

## Checkpoint 1: The "Silent Killer" Check (Build Warnings)

**Goal:** Detect reflection/dynamic code that will crash at runtime.

### Command

```powershell
dotnet publish src/Api/NetCommerce.Api.csproj -c Release -r linux-x64 -p:PublishAot=true
```

### Pass/Fail Criteria

#### ✅ PASS: Zero IL2026/IL3050 Warnings

```plaintext
NetCommerce.Api -> /artifacts/bin/NetCommerce.Api/release_linux-x64/publish/
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

#### ⚠️ CAUTION: Warnings in Non-Critical Paths

```plaintext
warning IL2026: ServiceCollectionExtensions.cs(98,23): Using 'WriteAsJsonAsync<T>'...
warning IL3050: MigrationExtensions.cs(29,19): Using 'MigrateAsync'...
```

**Verdict:** Acceptable if isolated to:
- Admin endpoints (not critical path)
- EF migrations (use migration bundles in production)
- OpenAPI generation (not used in production)

#### ❌ FAIL: Warnings in Critical Paths

```plaintext
warning IL2026: ProductEndpoints.cs(165,13): Using 'JsonSerializer.Serialize'...
warning IL2026: OrderHandler.cs(42,8): Using 'Activator.CreateInstance'...
```

**Action:** STOP. Fix these immediately:
- Register missing types in `ApiJsonContext`
- Replace `Activator.CreateInstance` with explicit constructors
- Add `[DynamicallyAccessedMembers]` attributes where necessary

### Current Status

Based on Phase 6 completion, we have **16 warnings** - all in non-critical paths:
- 7x CS8669 (nullable annotations - cosmetic)
- 4x IL2026/IL3050 (JSON serialization in admin/exception handlers)
- 2x IL2026/IL3050 (LINQ AsQueryable in admin endpoints)
- 1x IL3050 (EF migrations - use bundles in prod)

**Verdict:** ✅ PASS (no critical path warnings)

---

## Checkpoint 2: The "Ghost Code" Check (Wolverine Code Generation)

**Goal:** Verify Wolverine generated static handlers before AOT compilation.

### Command

```powershell
cd src/Api
dotnet run -- codegen write
```

### Expected Output

```plaintext
Wolverine: Writing source code to /src/src/Api/Internal/Generated/WolverineHandlers/...
    Generating CreateOrderHandler.cs
    Generating OrderSubmittedHandler.cs
    Generating PaymentRequestedHandler.cs
    ...
Wolverine code generation complete.
```

### Verification Steps

1. Navigate to `src/Api/Internal/Generated/WolverineHandlers/`
2. Open `CreateOrderHandler.cs` (or any handler)
3. Verify it contains generated C# code:

```csharp
// Expected content
public static class CreateOrderHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateOrderCommand command,
        IMessageBus bus,
        OrderingDbContext db,
        CancellationToken cancellationToken)
    {
        // ... generated handler code
    }
}
```

### Pass/Fail Criteria

| Condition | Verdict | Action |
|-----------|---------|--------|
| ✅ Folder exists with .cs files containing handlers | **PASS** | Continue to Checkpoint 3 |
| ⚠️ Folder empty but no errors | **PARTIAL** | TypeLoadMode.Auto will fallback to runtime generation (slower startup) |
| ❌ Folder missing or codegen crashes | **FAIL** | Fix Wolverine configuration in Program.cs |

### Troubleshooting

**Error: "Could not load file or assembly 'System.Data.SqlClient'"**

**Cause:** Wolverine codegen tries to start the app (requires DB connection).

**Fix:** Use TypeLoadMode.Auto instead of Static:
```csharp
opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
```

**Note:** Per Phase 4, we're using `TypeLoadMode.Auto` which allows runtime fallback if codegen fails.

---

## Checkpoint 3: The "Binary Anatomy" Check (Image Size & Composition)

**Goal:** Verify the artifact is truly Native AOT (no .NET runtime).

### Command

```powershell
docker build -t netcommerce-aot -f src/Api/Dockerfile .
docker images netcommerce-aot
```

### Expected Output

```plaintext
REPOSITORY          TAG       SIZE
netcommerce-aot     latest    65MB
```

### Pass/Fail Criteria

| Image Size | Verdict | Analysis |
|------------|---------|----------|
| **< 100 MB** | ✅ **PASS** | Native AOT with chiseled runtime |
| 100-150 MB | ⚠️ **PARTIAL** | Likely Native AOT but not chiseled (using runtime-deps:noble) |
| **> 200 MB** | ❌ **FAIL** | JIT build (includes full .NET runtime) |

### Deep Inspection (Optional)

Extract the binary and verify it's native:

```powershell
# Create temporary container
docker create --name temp netcommerce-aot
docker cp temp:/app/NetCommerce.Api ./NetCommerce.Api
docker rm temp

# Check file type (on Linux/WSL)
file NetCommerce.Api
# Expected: "ELF 64-bit LSB executable, x86-64"
# NOT: "PE32+ executable" (Windows) or script

# Check for .NET runtime DLLs (should be absent)
docker run --rm netcommerce-aot ls -la /app | grep -E "coreclr|clrjit"
# Expected: No output (DLLs don't exist)
```

### Chiseled Runtime Verification

```powershell
# Verify no shell
docker run --rm netcommerce-aot /bin/sh
# Expected: "executable file not found in $PATH"

# Verify no package manager
docker run --rm netcommerce-aot apt --version
# Expected: "executable file not found in $PATH"

# Verify non-root user
docker run --rm netcommerce-aot id
# Expected: "uid=1654(app) gid=1654(app)"
```

---

## Checkpoint 4: The "Smoke Test" (Runtime Startup)

**Goal:** Verify the native binary starts without crashes.

### Prerequisites

Start dependencies using Aspire or Docker Compose:

```powershell
# Option A: Aspire (starts all dependencies)
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj

# Option B: Docker Compose (if you have one)
docker-compose up -d postgres redis keycloak meilisearch
```

### Command

```powershell
# Get database connection string from Aspire dashboard or docker inspect
docker run --rm -it -p 8080:8080 `
  -e ConnectionStrings__NetCommerce="Host=host.docker.internal;Database=netcommerce;Username=test;Password=test123" `
  -e ConnectionStrings__Redis="host.docker.internal:6379" `
  -e Keycloak__Authority="http://host.docker.internal:8080/realms/netcommerce" `
  --name netcommerce-aot `
  netcommerce-aot
```

**Note:** Use `host.docker.internal` on Windows/Mac to access host services from container.

### Expected Output (PASS)

```plaintext
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:8080
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

**Startup Time:** Should appear in **< 100ms** (vs. 2-3 seconds for JIT).

### Failure Scenarios

#### ❌ FAIL: MissingMethodException

```plaintext
Unhandled exception. System.MissingMethodException: Method not found: 'Void MyClass.MyMethod()'
   at <Module>.wmain(IntPtr, Int32, Int32)
```

**Cause:** Reflection-based code was trimmed by the linker.

**Fix:**
1. Find the offending code (usually in DI registration)
2. Add `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]` attribute
3. Or refactor to use explicit registration

#### ❌ FAIL: JsonException at Startup

```plaintext
System.Text.Json.JsonException: The JSON value could not be converted to NetCommerce.Catalog.Application.Products.DTOs.ProductDto
```

**Cause:** DTO not registered in `ApiJsonContext`.

**Fix:** Add to `src/Api/Serialization/ApiJsonContext.cs`:
```csharp
[JsonSerializable(typeof(ProductDto))]
```

#### ❌ FAIL: DependencyResolutionException

```plaintext
System.InvalidOperationException: No service for type 'NetCommerce.Catalog.Application.IProductRepository' has been registered.
```

**Cause:** Service not registered (Scrutor was removed in Phase 1).

**Fix:** Add explicit registration in `Program.cs` or module extensions:
```csharp
services.AddScoped<IProductRepository, ProductRepository>();
```

---

## Checkpoint 5: The "Thread-Pull" (Functional Verification)

**Goal:** Exercise all three critical AOT paths: Serialization, EF Core, Wolverine.

### Prerequisites

Ensure container is running from Checkpoint 4.

---

### Test A: Endpoint Registration & Health Check

**What It Tests:** `MapNetCommerceEndpoints()` explicit registration (Phase 6).

```powershell
curl http://localhost:8080/health/ready
```

**Expected:**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0234567"
}
```

**Verdict:**
- ✅ **200 OK**: Endpoint registration works
- ❌ **404 Not Found**: Endpoint was trimmed; check `EndpointRegistrationExtensions.cs`

---

### Test B: JSON Serialization & EF Core Read

**What It Tests:**
- `ApiJsonContext` source-generated serialization (Phase 5)
- EF Core query compilation
- DTO mapping

```powershell
curl http://localhost:8080/api/v1/products
```

**Expected:**
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "Product Name",
      "price": 29.99,
      "currency": "GEL"
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 1
}
```

**Verdict:**
- ✅ **200 OK with JSON**: Serialization + EF Core working
- ❌ **500 Internal Server Error**: Check logs for JsonException or EF Core errors

**Possible Failures:**

```plaintext
# JSON Serialization
JsonException: Cannot get metadata for type 'ProductDto'
→ Fix: Add [JsonSerializable(typeof(ProductDto))] to ApiJsonContext

# EF Core
InvalidOperationException: The entity type 'Product' was not found
→ Fix: Ensure DbContext is registered and configured correctly
```

---

### Test C: The Full "Write" Cycle (Wolverine + Outbox + EF Write)

**What It Tests:**
- Request deserialization (ApiJsonContext)
- Wolverine message dispatch (generated handlers)
- EF Core transactional outbox (write operations)
- Saga state serialization
- Response serialization

#### Setup: Get Access Token (if auth enabled)

```powershell
# Skip if authentication is disabled for testing
# Otherwise, get token from Keycloak
$token = "Bearer eyJhbGc..."
```

#### Execute: Create Order

```powershell
curl -X POST http://localhost:8080/api/v1/orders `
  -H "Content-Type: application/json" `
  -H "X-Idempotency-Key: test-aot-verification-001" `
  -H "Authorization: $token" `
  -d '{
    "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "customerEmail": "test@example.com",
    "customerName": "Test User",
    "items": [
      {
        "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "quantity": 2
      }
    ],
    "shippingAddress": {
      "street": "123 Main St",
      "city": "Tbilisi",
      "state": "Tbilisi",
      "postalCode": "0100",
      "country": "Georgia",
      "recipientName": "Test User",
      "phoneNumber": "+995555123456"
    },
    "billingAddress": {
      "street": "123 Main St",
      "city": "Tbilisi",
      "state": "Tbilisi",
      "postalCode": "0100",
      "country": "Georgia",
      "recipientName": "Test User",
      "phoneNumber": "+995555123456"
    },
    "paymentMethod": "stripe"
  }'
```

**Expected:**
```json
{
  "orderId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "orderNumber": "ORD-20260204-001",
  "status": "Submitted"
}
```

**Verdict:**
- ✅ **201 Created**: Full AOT compliance achieved! 🎉
- ❌ **400 Bad Request**: Check validation logic (likely JSON deserialization issue)
- ❌ **500 Internal Server Error**: Critical failure - check logs

**Possible Failures:**

```plaintext
# Wolverine Handler Not Found
InvalidOperationException: No handler found for message type 'CreateOrderCommand'
→ Fix: Verify Wolverine codegen ran (Checkpoint 2)

# Saga Serialization
JsonException: Cannot serialize type 'Money'
→ Fix: Ensure Money is in ApiJsonContext (Phase 5)

# EF Core Outbox
InvalidOperationException: Wolverine outbox not configured
→ Fix: Verify UseWolverine().IntegrateWithEfCore() in Program.cs
```

#### Verify Background Processing

Check that Wolverine processed the message:

```powershell
# Check order status (should be in "ReservingInventory" or later)
curl http://localhost:8080/api/v1/orders/7c9e6679-7425-40de-944b-e07fc1f90ae7
```

**Expected:**
```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "AwaitingPayment", // or "ReservingInventory"
  "totalAmount": 59.98
}
```

---

## Troubleshooting: "It Crashed!"

### Step 1: Enable Stack Traces

Native AOT disables stack traces for security. Re-enable temporarily:

**Edit `src/Api/NetCommerce.Api.csproj`:**
```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <StackTraceSupport>true</StackTraceSupport>  <!-- Add this -->
  <IlcGenerateStackTraceData>true</IlcGenerateStackTraceData>  <!-- Add this -->
</PropertyGroup>
```

**Rebuild:**
```powershell
docker build -t netcommerce-aot-debug -f src/Api/Dockerfile .
```

### Step 2: Identify Exception Type

| Exception | Cause | Fix |
|-----------|-------|-----|
| **MissingMetadataException** | Used reflection/Type.GetMethod() | Remove reflection or add `[DynamicallyAccessedMembers]` |
| **JsonException** | DTO not in ApiJsonContext | Add `[JsonSerializable(typeof(YourDto))]` |
| **InvalidOperationException (DI)** | Service not registered | Add explicit `services.AddScoped<T>()` |
| **InvalidOperationException (EF)** | Entity not in model | Check DbContext configuration |
| **FileNotFoundException** | Assembly trimmed | Add `<TrimmerRootAssembly>` in .csproj |

### Step 3: Check Wolverine Outbox Tables

```sql
-- Connect to Postgres
SELECT COUNT(*) FROM wolverine.wolverine_outgoing_envelopes;
-- Should be 0 or low (messages are processed quickly)

SELECT COUNT(*) FROM wolverine.saga_state WHERE saga_type = 'OrderFulfillmentSaga';
-- Should match number of orders created
```

### Step 4: Analyze IL2026/IL3050 Warnings

Re-run publish with verbosity:

```powershell
dotnet publish src/Api/NetCommerce.Api.csproj -c Release -r linux-x64 -p:PublishAot=true -v detailed
```

Search for "warning IL" and fix the top 3 most frequent warnings.

---

## Final Sign-Off Checklist

| Checkpoint | Status | Notes |
|------------|--------|-------|
| 1. Build Warnings | ⬜ | Zero critical path IL2026/IL3050? |
| 2. Wolverine Codegen | ⬜ | Handler .cs files exist? |
| 3. Image Size | ⬜ | < 100MB? |
| 4. Startup | ⬜ | "Now listening" appears in <100ms? |
| 5A. Health Check | ⬜ | GET /health/ready returns 200? |
| 5B. Read Path | ⬜ | GET /api/v1/products returns JSON? |
| 5C. Write Path | ⬜ | POST /api/v1/orders returns 201? |

**All ✅ = Production Ready** 🚀

---

## Performance Baseline (Post-Verification)

Record these metrics for regression testing:

```powershell
# Startup time
docker run --rm netcommerce-aot | Select-String "Application started"
# Target: < 100ms

# Memory usage (idle)
docker stats netcommerce-aot --no-stream
# Target: < 120 MB

# Request latency (P95)
# Use NetCommerce.LoadTests or k6
# Target: < 20ms for read endpoints
```

---

## References

- [Phase 1-7: Native AOT Migration Steps](./NATIVE_AOT_MIGRATION.md)
- [Docker Build Guide](./NATIVE_AOT_DOCKER_BUILD.md)
- [.NET Native AOT Deployment Docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Troubleshooting IL2026/IL3050](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings)

---

**Status:** Ready for execution after Phase 7 Dockerfile implementation
