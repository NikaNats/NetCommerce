# Native AOT Verification

Five-checkpoint verification protocol to validate Native AOT builds are production-ready.

## Overview

NetCommerce supports Native AOT (Ahead-of-Time) compilation for production deployments. AOT eliminates the JIT compiler at runtime, producing a single native binary with faster startup, lower memory usage, and a smaller attack surface.

The verification protocol validates five critical aspects of an AOT build before deployment.

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- PowerShell 7+
- Running PostgreSQL and Redis instances (or Aspire)

## Running Verification

```powershell
# Run all 5 checkpoints
.\scripts\Verify-NativeAOT.ps1

# Run specific checkpoints
.\scripts\Verify-NativeAOT.ps1 -CheckpointsToRun "1,2,3"

# Skip Docker build (use existing image)
.\scripts\Verify-NativeAOT.ps1 -SkipBuild

# Custom database connection
.\scripts\Verify-NativeAOT.ps1 -DatabaseConnectionString "Host=mydb;Database=netcommerce;..."
```

## Checkpoint 1: The "Silent Killer" Check

**Purpose:** Detect IL2026/IL3050 build warnings that indicate runtime reflection/dynamic-code usage — silent failures in AOT.

**Process:**
```
dotnet publish src/Api/NetCommerce.Api.csproj -c Release -r linux-x64 -p:PublishAot=true
```

**Classification:**

| Outcome | Warning Pattern | Result |
|---|---|---|
| **Critical** | Warnings in `ProductEndpoints`, `OrderHandler`, `BasketEndpoints`, `InventoryEndpoints` | FAIL |
| **Non-critical** | Warnings in admin/migration code only | PASS (with warnings) |
| **Clean** | Zero IL2026/IL3050 warnings | PASS |

**Current status:** 16 warnings, all in non-critical paths (admin, migration, diagnostic code). Production endpoints are clean.

### Troubleshooting IL Warnings

| Warning | Cause | Resolution |
|---|---|---|
| `IL2026` | `RequiresUnreferencedCode` — method uses reflection | Use source generators or `[DynamicDependency]` |
| `IL3050` | `RequiresDynamicCode` — method requires runtime code gen | Use `JsonSerializerContext` or pre-compiled alternatives |

## Checkpoint 2: The "Ghost Code" Check

**Purpose:** Verify Wolverine message handler code generation completes successfully.

**Process:**
```
cd src/Api
dotnet run -- codegen write
```

**What it checks:**
- Wolverine generates handler source files in `Internal/Generated/WolverineHandlers/`
- Generated handler count is > 0
- Fallback: if `TypeLoadMode.Auto` is configured, runtime codegen is acceptable

**Configuration in `Program.cs`:**
```csharp
opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
```

`TypeLoadMode.Static` forces pre-generated handlers — if codegen fails, the application will fail to start, catching issues early.

## Checkpoint 3: The "Binary Anatomy" Check

**Purpose:** Verify the Docker image meets AOT container requirements.

**Checks performed:**

| Check | Criteria | Expected |
|---|---|---|
| Image size | < 100 MB | Chiseled AOT binary |
| Image size | 100–150 MB | AOT but not chiseled |
| Image size | > 150 MB | Likely JIT build (FAIL) |
| Shell access | `/bin/sh` returns "not found" | Chiseled runtime |
| User ID | `uid=1654` | Non-root AppUser |

**Chiseled container properties:**
- No shell (`/bin/sh` not available)
- No package manager
- Non-root user (UID 1654)
- Minimal filesystem — only .NET runtime dependencies

### AOT Dockerfile Structure

```dockerfile
# Stage 1: Build with AOT
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# ... restore, build, publish with -p:PublishAot=true

# Stage 2: Chiseled runtime (no shell, no root)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
USER 1654
COPY --from=build /app/publish .
ENTRYPOINT ["./NetCommerce.Api"]
```

## Checkpoint 4: The "Smoke Test"

**Purpose:** Verify the AOT binary starts successfully in a container.

**Process:**
1. Start container with database and Redis connection strings
2. Wait for "Now listening on" log message
3. Maximum wait: 10 seconds

**Success criteria:**
- Application starts without exceptions
- No `MissingMethodException` (indicates trimmed required code)
- No `JsonException` (indicates missing `JsonSerializerContext` entries)
- Startup time < 100 ms (typical for AOT)

**Failure indicators:**

| Error | Cause | Resolution |
|---|---|---|
| `MissingMethodException` | Required method was trimmed | Add `[DynamicDependency]` or rooting |
| `JsonException` | Type not in `ApiJsonContext` | Add type to source-generated JSON context |
| `TypeLoadException` | Type was trimmed by linker | Add `[DynamicallyAccessedMembers]` |
| Timeout (>10s) | Not AOT — JIT compilation overhead | Verify `PublishAot=true` in build |

## Checkpoint 5: The "Thread-Pull"

**Purpose:** Functional verification of critical runtime paths.

### Test 5A: Health Check

```http
GET http://localhost:8080/health/ready
Expected: 200 OK
```

Verifies middleware pipeline, DI container, and database connectivity all work in AOT.

### Test 5B: JSON Serialization and EF Core Read

```http
GET http://localhost:8080/api/v1/products
Expected: 200 OK with JSON response
```

Verifies:
- `ApiJsonContext` source-generated JSON serialization
- EF Core query execution
- Response serialization pipeline

### Test 5C: Full Write Cycle (Manual)

```http
POST http://localhost:8080/api/v1/orders
Authorization: Bearer <token>
X-Idempotency-Key: <guid>
```

Verifies:
- Wolverine handler dispatch
- Transactional outbox (EF Core + Wolverine)
- Saga state machine creation

This test requires authentication and is performed manually.

## AOT Compatibility Measures

### JSON Source Generation

All API request/response types are registered in `ApiJsonContext`:

```csharp
[JsonSerializable(typeof(PaginatedResponse<ProductResponse>))]
[JsonSerializable(typeof(CreateProductCommand))]
// ... all endpoint types
internal partial class ApiJsonContext : JsonSerializerContext { }
```

### Wolverine Static Code Generation

Pre-generated handlers eliminate runtime Reflection.Emit:

```csharp
opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
```

### EF Core AOT Support

EF Core 10 provides improved AOT support. Query compilation is handled by the Npgsql provider's built-in AOT compatibility.

### Build Configuration

From `Directory.Build.props`:

```xml
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<EnableAotAnalyzer>true</EnableAotAnalyzer>
<TrimmerDefaultAction>link</TrimmerDefaultAction>
```

## Performance Comparison

| Metric | JIT | Native AOT |
|---|---|---|
| Startup time | ~800 ms | < 100 ms |
| Image size | ~220 MB | < 80 MB |
| Memory (idle) | ~120 MB | ~45 MB |
| First request latency | ~200 ms | < 5 ms |
| Throughput (steady state) | Baseline | ~Same |

## Verification Summary

The script outputs a pass/fail summary:

```
Verification Summary
========================================
  checkpoint1: ✅ PASS
  checkpoint2: ✅ PASS
  checkpoint3: ✅ PASS
  checkpoint4: ✅ PASS
  checkpoint5: ✅ PASS

Total: 5 / 5 passed
🎉 ALL CHECKPOINTS PASSED - PRODUCTION READY 🚀
```

If any checkpoint fails, the script exits with code 1 and directs to this document for troubleshooting.

## Related Documentation

- [Deployment](DEPLOYMENT.md) — Docker builds and AOT deployment
- [Architecture](ARCHITECTURE.md) — AOT design decisions
- [Troubleshooting](TROUBLESHOOTING.md) — common AOT issues
- [Contributing](../CONTRIBUTING.md) — JSON source generation requirements
