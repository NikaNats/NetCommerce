# Troubleshooting

Common issues, error patterns, and resolution steps for NetCommerce development and production environments.

## Development Environment

### Docker / Aspire Startup Failures

**Problem:** `dotnet run --project src/NetCommerce.AppHost` fails to start containers.

**Resolution:**
1. Verify Docker Desktop is running
2. Check no port conflicts: `netstat -ano | findstr "5432 6379 8080"`
3. Clean up stale containers: `docker rm -f $(docker ps -aq)`
4. Clear Aspire state: delete `.aspire` folder in project root
5. Reset Docker: Docker Desktop → Troubleshoot → Clean/Purge data

### Port Conflicts

**Problem:** `Address already in use` when starting the application.

**Common ports:**

| Port | Service | Fix |
|---|---|---|
| 5432 | PostgreSQL | Stop local PostgreSQL or change Aspire port |
| 6379 | Redis | Stop local Redis |
| 8080 | Keycloak / API | Change in `launchSettings.json` |
| 7700 | MeiliSearch | Stop local MeiliSearch |
| 5341 | Seq | Stop local Seq |

### Database Migration Issues

**Problem:** `Npgsql.PostgresException: relation "..." does not exist`.

**Resolution:**
```powershell
# Migrations are applied automatically in Development
# If still failing, verify connection string
dotnet ef database update --project src/Catalog/Catalog.Infrastructure --startup-project src/Api/NetCommerce.Api.csproj

# Verify schemas exist
psql -h localhost -U postgres -d netcommerce -c "\dn"
```

### Keycloak Realm Configuration

**Problem:** `401 Unauthorized` on all authenticated endpoints.

**Resolution:**
1. Verify Keycloak is running: `http://localhost:8080`
2. Check realm `netcommerce` exists
3. Verify client configuration matches `Auth:ClientId` / `Auth:Audience`
4. Check role mappings (admin, vendor, customer)
5. Verify realm URL matches `Auth:Authority` config

### MeiliSearch Sync

**Problem:** Product search returns no results despite existing products.

**Resolution:**
1. Verify MeiliSearch is running: `GET http://localhost:7700/health`
2. Check search index: `GET http://localhost:7700/indexes`
3. Trigger index rebuild by updating a product
4. Check Seq logs for search sync errors

## Build Issues

### AOT Build Warnings

**Problem:** `warning IL2026` or `warning IL3050` during publish.

**Resolution:**
- **Critical path warnings** (endpoints, handlers): Must be fixed before deployment
  - Use `JsonSerializerContext` instead of reflection-based serialization
  - Replace `Type.GetType()` calls with static type references
  - Add `[DynamicDependency]` attributes for required types
- **Admin/migration path warnings**: Acceptable, document in release notes

See [Native AOT Verification](NATIVE_AOT_VERIFICATION.md) for the full 5-checkpoint protocol.

### TreatWarningsAsErrors

**Problem:** Build fails in Release/CI with compiler warnings.

**Resolution:**
```powershell
# Build with Debug configuration to see warnings without failing
dotnet build -c Debug

# Fix the warnings, then verify
dotnet build -c Release
```

`TreatWarningsAsErrors` is enabled in `Directory.Build.props` for Release and CI builds only.

## Test Failures

### Integration Tests (Testcontainers)

**Problem:** Integration tests fail with container startup errors.

**Resolution:**
1. Verify Docker Desktop is running
2. Ensure Testcontainers can pull images: `docker pull postgres:17` and `docker pull redis:8`
3. Check available disk space (containers need ~500 MB)
4. Increase Docker memory allocation if tests OOM

### Concurrency Stress Tests

**Problem:** `ConcurrentInventoryStressTests` flaky failures.

**Resolution:**
- These tests are inherently timing-sensitive
- Run with `--filter "FullyQualifiedName~Concurrent"` in isolation
- Ensure no other database activity during test run
- Check `ReservationCleanupJob` is not interfering

### Architecture Tests

**Problem:** `NetArchTest` validation failures.

**Resolution:**
```
Domain projects must not reference Infrastructure or Application
Application projects must not reference Infrastructure
Infrastructure CAN reference Application and Domain
```

Verify project references: `dotnet list reference` on the failing project.

## Runtime Issues

### Wolverine Serialization

**Problem:** `System.Text.Json.JsonException` when deserializing saga state or outbox messages.

**Cause:** Fully qualified type names changed between phases (SharedKernel → Domain.Shared).

**Resolution:**
1. Clear Wolverine tables before deploying type namespace changes:
   ```sql
   TRUNCATE ordering.wolverine_outgoing_envelopes;
   TRUNCATE ordering.wolverine_incoming_envelopes;
   ```
2. Use `[Obsolete]` types as migration bridge (already in place for Phase 5)
3. See [Phase 5 Serialization Migration](PHASE_5_SERIALIZATION_MIGRATION.md)

### Outbox Message Backlog

**Problem:** Growing outbox tables indicate message delivery failures.

**Monitoring:**
```sql
SELECT COUNT(*) FROM ordering.wolverine_outgoing_envelopes;
SELECT COUNT(*) FROM ordering.wolverine_incoming_envelopes;
```

**Resolution:**
1. Check Seq logs for handler exceptions
2. Verify target handlers are registered
3. Check DLQ for permanently failed messages
4. If backlog is stuck, restart the application to reset Wolverine's internal state

### Saga Stuck States

**Problem:** Orders stuck in `ProcessingPayment` or `ReservingInventory` states.

**Resolution:**
```http
# Check stuck sagas
GET /api/v1/orders/manual-intervention
Authorization: Bearer <admin-token>

# Inspect specific saga
GET /api/admin/orders/{orderId}/saga-details
Authorization: Bearer <admin-elevated-token>
```

See [Operations](OPERATIONS.md) for recovery procedures.

### Webhook Delivery Failures

**Problem:** Stripe webhook events are not being processed.

**Checklist:**
1. Verify webhook endpoint URL in Stripe dashboard
2. Check `Stripe:WebhookSecret` configuration matches Stripe dashboard signing secret
3. Review Seq logs: `@Message like 'Stripe webhook%'`
4. Check `ProcessedWebhookEvent` table for idempotency duplicates
5. Verify webhook endpoint is accessible (not behind auth middleware)
6. Check DLQ for failed payment commands

### Connection Pool Exhaustion

**Problem:** `NpgsqlException: The connection pool has been exhausted`.

**Resolution:**
1. Check active connections: `SELECT count(*) FROM pg_stat_activity WHERE datname = 'netcommerce'`
2. Look for long-running transactions: `SELECT * FROM pg_stat_activity WHERE state = 'idle in transaction'`
3. Check for missing `await using` on DbContext scopes
4. Increase pool size in connection string: `;Maximum Pool Size=200`
5. Restart application as immediate relief

### Redis Connection

**Problem:** `StackExchange.Redis.RedisConnectionException`.

**Resolution:**
1. Verify Redis is running: `redis-cli ping`
2. Check connection string in configuration
3. Verify Redis memory: `redis-cli info memory`
4. If using Docker: ensure container is healthy

## Performance Issues

### Slow API Responses

**Diagnostic sequence:**
1. Check Seq for slow query logs
2. Verify database indexes exist on frequently queried columns
3. Check Redis cache hit rates
4. Monitor connection pool utilization
5. Check if MeiliSearch is healthy (search endpoints)

### High Memory Usage

**Resolution:**
1. Verify using AOT build (lower baseline memory)
2. Check for growing in-process caches
3. Monitor Redis memory independently
4. Review HybridCache TTL settings (L1: 5 min, L2: 60 min)

## Error Response Reference

### Standard Error Format

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation Error",
  "status": 400,
  "detail": "Description of the problem",
  "errors": {
    "fieldName": ["Error message"]
  }
}
```

### Common HTTP Status Codes

| Status | Meaning | Common Cause |
|---|---|---|
| 400 | Bad Request | Validation failure, missing idempotency key |
| 401 | Unauthorized | Missing or expired JWT token |
| 403 | Forbidden | Insufficient role/permissions |
| 404 | Not Found | Resource does not exist |
| 409 | Conflict | Concurrency conflict, duplicate resource |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Unhandled exception |

## Related Documentation

- [Operations](OPERATIONS.md) — monitoring and incident response
- [Testing](TESTING.md) — running and debugging tests
- [Deployment](DEPLOYMENT.md) — production configuration
- [Native AOT Verification](NATIVE_AOT_VERIFICATION.md) — AOT build troubleshooting
