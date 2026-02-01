# NetCommerce Troubleshooting Guide

> **Common issues, debugging techniques, and solutions**

---

## Table of Contents

1. [Quick Diagnostic Commands](#quick-diagnostic-commands)
2. [Startup Issues](#startup-issues)
3. [Database Issues](#database-issues)
4. [Authentication Issues](#authentication-issues)
5. [Messaging Issues](#messaging-issues)
6. [Performance Issues](#performance-issues)
7. [Integration Issues](#integration-issues)
8. [Test Failures](#test-failures)
9. [Common Error Codes](#common-error-codes)
10. [FAQ](#faq)

---

## Quick Diagnostic Commands

### Health Check

```powershell
# Check if API is running and healthy
curl http://localhost:5000/health/ready

# Expected response: {"status":"Healthy","entries":{...}}
```

### Logs

```powershell
# View API logs (Aspire)
# Open Aspire dashboard: https://localhost:17235

# Seq queries
# Open Seq: http://localhost:5341
```

### Database

```powershell
# Connect to PostgreSQL
docker exec -it netcommerce-postgres psql -U postgres -d ordering

# Check connection count
SELECT count(*) FROM pg_stat_activity;

# Check active queries
SELECT pid, now() - query_start AS duration, query
FROM pg_stat_activity WHERE state = 'active';
```

### Redis

```powershell
# Connect to Redis
docker exec -it netcommerce-redis redis-cli

# Check memory
INFO memory

# Check keys
KEYS *
```

---

## Startup Issues

### "Cannot connect to Docker daemon"

**Symptoms:**
```
Error: Cannot connect to Docker daemon at unix:///var/run/docker.sock
```

**Cause:** Docker Desktop not running.

**Solution:**
```powershell
# Windows
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"

# Wait for Docker to start, then retry
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

---

### "Port already in use"

**Symptoms:**
```
System.Net.Sockets.SocketException: Address already in use
```

**Cause:** Previous instance still running or port conflict.

**Solution:**
```powershell
# Find process using port (e.g., 5000)
netstat -ano | findstr :5000

# Kill the process
taskkill /PID <pid> /F

# Or stop all containers
docker compose down
docker stop $(docker ps -q)
```

---

### "Failed to create database"

**Symptoms:**
```
Npgsql.PostgresException: 42P04: database "catalog" already exists
```

**Cause:** Database from previous run exists.

**Solution:**
```powershell
# Option 1: Drop and recreate (loses data)
docker exec -it netcommerce-postgres psql -U postgres -c "DROP DATABASE catalog;"

# Option 2: Reset all Aspire volumes (fresh start)
docker volume prune -f
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

---

### "Keycloak realm import failed"

**Symptoms:**
```
Error importing realm: Conflict detected
```

**Cause:** Realm already exists from previous run.

**Solution:**
```powershell
# Remove Keycloak volume
docker volume rm netcommerce_keycloak-data

# Restart
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

---

## Database Issues

### "Connection pool exhausted"

**Symptoms:**
```
Npgsql.NpgsqlException: The connection pool has been exhausted
```

**Cause:** Too many concurrent connections, connections not being released.

**Solution:**
```csharp
// 1. Increase pool size in connection string
"Host=...;Pooling=true;Minimum Pool Size=10;Maximum Pool Size=100"

// 2. Ensure DbContext is disposed (use 'using' or scoped lifetime)
await using var context = new OrderingDbContext();

// 3. Check for long-running transactions
SELECT * FROM pg_stat_activity WHERE state = 'idle in transaction';
```

---

### "Concurrency conflict"

**Symptoms:**
```
Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
The database operation was expected to affect 1 row(s), but actually affected 0 row(s)
```

**Cause:** Row was modified by another process (optimistic concurrency).

**Solution:**
```csharp
// This is expected behavior under high concurrency
// Wolverine handlers should be configured to retry
opts.OnException<DbUpdateConcurrencyException>()
    .RetryWithCooldown(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1));
```

---

### "Migration failed"

**Symptoms:**
```
Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving
42P01: relation "orders" does not exist
```

**Cause:** Migrations not applied.

**Solution:**
```powershell
# Apply migrations
dotnet ef database update --project src/Ordering/Ordering.Infrastructure

# Or run with startup migration (if configured)
dotnet run --project src/Api -- --migrate
```

---

### "Deadlock detected"

**Symptoms:**
```
Npgsql.PostgresException: 40P01: deadlock detected
```

**Cause:** Two transactions waiting on each other.

**Solution:**
```sql
-- Check for blocking queries
SELECT blocked.pid AS blocked_pid,
       blocked.query AS blocked_query,
       blocking.pid AS blocking_pid,
       blocking.query AS blocking_query
FROM pg_stat_activity blocked
JOIN pg_locks blocked_locks ON blocked.pid = blocked_locks.pid
JOIN pg_locks blocking_locks ON blocked_locks.locktype = blocking_locks.locktype
JOIN pg_stat_activity blocking ON blocking_locks.pid = blocking.pid
WHERE blocked_locks.granted = false AND blocking_locks.granted = true;

-- Kill blocking query if necessary
SELECT pg_terminate_backend(<blocking_pid>);
```

---

## Authentication Issues

### "401 Unauthorized"

**Symptoms:**
```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401
}
```

**Cause:** Missing, expired, or invalid token.

**Solution:**
```powershell
# 1. Check if token is present in request header
Authorization: Bearer <token>

# 2. Verify token is not expired
# Decode at jwt.io and check 'exp' claim

# 3. Check Keycloak is running
curl http://localhost:8080/health/ready

# 4. Verify audience matches
# Token 'aud' claim should include 'netcommerce-api'
```

---

### "Token introspection failed"

**Symptoms:**
```
Token introspection failed: Token marked as inactive by identity provider
```

**Cause:** Token was revoked or user was disabled in Keycloak.

**Solution:**
```powershell
# 1. This is expected if user was banned - verify in Keycloak
# Admin Console → Users → Find user → Check enabled status

# 2. If testing, get a new token:
curl -X POST http://localhost:8080/realms/netcommerce/protocol/openid-connect/token \
  -d "client_id=netcommerce-frontend" \
  -d "username=testuser" \
  -d "password=testpass" \
  -d "grant_type=password"
```

---

### "Invalid audience"

**Symptoms:**
```
Microsoft.IdentityModel.Tokens.SecurityTokenInvalidAudienceException:
IDX10214: Audience validation failed
```

**Cause:** Token was issued for different audience.

**Solution:**
```csharp
// Check appsettings.json
"Auth": {
  "Audience": "netcommerce-api"  // Must match token 'aud' claim
}

// Or in Keycloak, add audience mapper to client
```

---

### "Role not found in claims"

**Symptoms:**
```
User.IsInRole("admin") returns false even though user has admin role
```

**Cause:** Role claims not transformed from Keycloak's nested format.

**Solution:**
```csharp
// Verify OidcRoleClaimsTransformation is registered
services.AddTransient<IClaimsTransformation, OidcRoleClaimsTransformation>();

// Check Keycloak token has realm_access.roles:
{
  "realm_access": {
    "roles": ["admin", "customer"]
  }
}
```

---

## Messaging Issues

### "Message not being processed"

**Symptoms:**
- Command returns success but side effects don't happen
- Integration events not triggering handlers

**Cause:** Handler not discovered or misconfigured.

**Solution:**
```csharp
// 1. Verify handler assembly is registered
opts.Discovery.IncludeAssembly(typeof(OrderingModule).Assembly);

// 2. Verify handler follows conventions
[WolverineHandler]
public static class MyHandler
{
    // Method must be named 'Handle' and message must be first parameter
    public static void Handle(MyCommand command, ILogger logger) { }
}

// 3. Check Wolverine logs for routing
// Look for: "Routing MyCommand to..."
```

---

### "Messages stuck in outbox"

**Symptoms:**
```sql
SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes WHERE status = 'pending';
-- Returns > 0 and growing
```

**Cause:** Outbox agent not running or target handler failing.

**Solution:**
```powershell
# 1. Check Wolverine background agent is running
# Look for in logs: "Starting Wolverine message processing"

# 2. Check for processing errors
SELECT * FROM wolverine.wolverine_outgoing_envelopes
WHERE status = 'error' LIMIT 10;

# 3. Manually retry stuck messages
await messageStore.RequeueAsync(envelopeId);
```

---

### "Saga not progressing"

**Symptoms:**
- Order stuck in "ProcessingPayment" state
- Saga timeout firing but no completion

**Cause:** Handler for expected event not working or event not published.

**Solution:**
```sql
-- 1. Check saga state
SELECT id, state->>'State', state->>'FailureReason'
FROM wolverine.saga_state
WHERE id = '<order-id>';

-- 2. Check if expected event was published
SELECT * FROM wolverine.wolverine_outgoing_envelopes
WHERE body::text LIKE '%<order-id>%';

-- 3. Check dead letters
SELECT * FROM wolverine.wolverine_incoming_envelopes
WHERE status = 'dead_letter';
```

---

### "Deserialization error in saga"

**Symptoms:**
```
System.Text.Json.JsonException: The JSON value could not be converted to NetCommerce.Domain.Shared.Money
```

**Cause:** Type namespace changed (Phase 5 migration issue).

**Solution:**
```powershell
# Option 1: Clear Wolverine tables (dev only)
TRUNCATE TABLE wolverine.saga_state CASCADE;
TRUNCATE TABLE wolverine.wolverine_outgoing_envelopes;
TRUNCATE TABLE wolverine.wolverine_incoming_envelopes;

# Option 2: Add legacy type resolver (see PHASE_5_SERIALIZATION_MIGRATION.md)
```

---

## Performance Issues

### "Slow API responses"

**Symptoms:**
- P99 latency > 500ms
- Requests timing out

**Investigation:**
```sql
-- Check slow queries
SELECT query, calls, mean_exec_time, total_exec_time
FROM pg_stat_statements
ORDER BY mean_exec_time DESC
LIMIT 10;

-- Check missing indexes
SELECT schemaname, tablename, indexrelname, idx_scan, idx_tup_read
FROM pg_stat_user_indexes
WHERE idx_scan = 0;
```

**Solution:**
```sql
-- Add missing indexes
CREATE INDEX CONCURRENTLY idx_orders_customer_id ON ordering.orders(customer_id);
CREATE INDEX CONCURRENTLY idx_orders_status ON ordering.orders(status);
```

---

### "Redis memory full"

**Symptoms:**
```
OOM command not allowed when used memory > 'maxmemory'
```

**Solution:**
```powershell
# Check memory usage
docker exec -it netcommerce-redis redis-cli INFO memory

# Clear cache (if acceptable)
docker exec -it netcommerce-redis redis-cli FLUSHDB

# Or increase memory limit
# In redis.conf: maxmemory 2gb
```

---

### "High CPU on database"

**Symptoms:**
- PostgreSQL using 100% CPU
- All queries slow

**Investigation:**
```sql
-- Find CPU-heavy queries
SELECT pid, now() - query_start AS duration, state, query
FROM pg_stat_activity
WHERE state = 'active'
ORDER BY duration DESC;

-- Check for sequential scans on large tables
SELECT relname, seq_scan, seq_tup_read, idx_scan, idx_tup_fetch
FROM pg_stat_user_tables
WHERE seq_scan > 1000
ORDER BY seq_tup_read DESC;
```

**Solution:**
```sql
-- Kill long-running query
SELECT pg_cancel_backend(<pid>);

-- Add index for frequent queries
EXPLAIN ANALYZE <slow query>;
-- Look for "Seq Scan" and add appropriate index
```

---

## Integration Issues

### "Meilisearch not indexing"

**Symptoms:**
- Products not appearing in search
- Search returns empty results

**Solution:**
```powershell
# 1. Check Meilisearch health
curl http://localhost:7700/health

# 2. Check index exists
curl http://localhost:7700/indexes -H "Authorization: Bearer <master-key>"

# 3. Check documents in index
curl http://localhost:7700/indexes/products/documents

# 4. Manually trigger reindex
POST /api/admin/search/reindex
```

---

### "Blob storage upload failed"

**Symptoms:**
```
Azure.RequestFailedException: The specified container does not exist
```

**Solution:**
```powershell
# 1. Check Azurite is running (dev)
docker ps | grep azurite

# 2. Create container if missing
az storage container create --name media --connection-string "<connection-string>"

# 3. Verify connection string in configuration
ConnectionStrings__blobs=...
```

---

## Test Failures

### "Integration tests fail: Docker not available"

**Symptoms:**
```
Docker API responded with status code=InternalServerError
```

**Solution:**
```powershell
# 1. Ensure Docker Desktop is running
# 2. Check Docker is accessible
docker ps

# 3. Restart Docker if needed
Restart-Service docker
```

---

### "Architecture tests fail"

**Symptoms:**
```
Domain layer should not depend on Infrastructure layer.
Failing types: NetCommerce.Catalog.Domain.Products.Product
```

**Cause:** Violation of Clean Architecture dependencies.

**Solution:**
```csharp
// Move the dependency to correct layer
// Domain should only depend on Kernel.Core
// Application should not depend on Infrastructure

// Check the failing type's using statements
// Remove any references to Infrastructure namespace
```

---

### "Respawn not cleaning database"

**Symptoms:**
- Test data from previous test affecting current test
- Flaky integration tests

**Solution:**
```csharp
// Ensure Respawner includes all schemas
_respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
{
    DbAdapter = DbAdapter.Postgres,
    SchemasToInclude = ["catalog", "inventory", "ordering", "payments", "wolverine"],
    TablesToInclude =
    [
        new Table("wolverine", "wolverine_incoming_envelopes"),
        new Table("wolverine", "wolverine_outgoing_envelopes")
    ]
});

// Call ResetAsync before each test
await _respawner.ResetAsync(connection);
```

---

## Common Error Codes

### API Error Codes

| Code | HTTP Status | Description | Solution |
|------|-------------|-------------|----------|
| `VALIDATION_ERROR` | 400 | Invalid request data | Check request body/parameters |
| `NOT_FOUND` | 404 | Resource doesn't exist | Verify ID is correct |
| `CONFLICT` | 409 | Operation conflicts | Retry with idempotency key |
| `UNAUTHORIZED` | 401 | Authentication required | Include valid token |
| `FORBIDDEN` | 403 | Insufficient permissions | Check user roles |
| `TOO_MANY_REQUESTS` | 429 | Rate limited | Wait and retry |
| `INTERNAL_ERROR` | 500 | Server error | Check logs, report bug |

### Domain Error Codes

| Code | Description |
|------|-------------|
| `Order.Empty` | Order has no items |
| `Order.InvalidState` | Operation invalid for current order state |
| `Order.AlreadySubmitted` | Order was already submitted |
| `Stock.InsufficientQuantity` | Not enough stock |
| `Stock.AlreadyReserved` | Stock already reserved |
| `Payment.Declined` | Payment was declined |
| `Money.NegativeAmount` | Amount cannot be negative |
| `Money.CurrencyMismatch` | Currencies don't match |

---

## FAQ

### Q: How do I reset everything and start fresh?

```powershell
# Stop everything
docker compose down -v
docker volume prune -f

# Restart
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

### Q: How do I see what's happening in Wolverine?

```csharp
// Enable detailed logging
opts.Policies.LogMessageProcessing();

// Check Seq for Wolverine logs
SourceContext = 'Wolverine'
```

### Q: How do I debug a specific order?

```sql
-- 1. Find order
SELECT * FROM ordering.orders WHERE order_number = 'ORD-XXXX';

-- 2. Check saga state
SELECT * FROM wolverine.saga_state WHERE id = '<order-id>';

-- 3. Check outbox messages
SELECT * FROM wolverine.wolverine_outgoing_envelopes
WHERE body::text LIKE '%<order-id>%';

-- 4. Check dead letters
SELECT * FROM wolverine.wolverine_incoming_envelopes
WHERE status = 'dead_letter';
```

### Q: How do I add a breakpoint in a message handler?

```csharp
// Handlers are just regular methods - add breakpoint normally
[WolverineHandler]
public static class MyHandler
{
    public static void Handle(MyCommand command)
    {
        Debugger.Break();  // Or set VS/Rider breakpoint
    }
}
```

### Q: How do I test token introspection locally?

```powershell
# 1. Enable introspection
Auth__IntrospectionEnabled=true

# 2. Get a token
$token = (Invoke-RestMethod -Uri "http://localhost:8080/realms/netcommerce/protocol/openid-connect/token" -Method Post -Body @{
    client_id = "netcommerce-frontend"
    username = "testuser"
    password = "testpass"
    grant_type = "password"
}).access_token

# 3. Make request and check introspection in logs
Invoke-RestMethod -Uri "http://localhost:5000/api/v1/orders" -Headers @{
    Authorization = "Bearer $token"
}
```

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce Platform Team

---

**Still stuck?** Create a GitHub Issue with:
1. Error message (full stack trace)
2. Steps to reproduce
3. Relevant logs from Seq
4. Environment (OS, .NET version, Docker version)
