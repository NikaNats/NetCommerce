# Operations

Operational procedures for monitoring, maintaining, and troubleshooting NetCommerce in production.

## Health Monitoring

### Health Check Endpoints

| Endpoint | Purpose | Frequency |
|---|---|---|
| `GET /health/ready` | Readiness — all dependencies available | Every 5s |
| `GET /health/live` | Liveness — process alive | Every 10s |

Readiness checks verify connectivity to:
- PostgreSQL (all 6 schemas)
- Redis
- MeiliSearch
- Keycloak (optional, degrades gracefully)

### OpenTelemetry

The application exports telemetry via OTLP to configured collectors:

| Signal | Instrumentation |
|---|---|
| **Traces** | ASP.NET Core, HTTP Client, EF Core, Redis |
| **Metrics** | Runtime, Process, ASP.NET Core, custom business metrics |
| **Logs** | Serilog → Seq (structured JSON) |

### Seq Log Queries

Common log queries for operational monitoring:

```
# Failed webhook processing
@Message like 'Stripe webhook%' and @Level = 'Error'

# Saga state transitions
SourceContext = 'OrderFulfillmentSaga'

# Dead letter queue entries
@Message like 'Dead letter%'

# Reconciliation alerts
SourceContext like '%Reconciliation%' and @Level >= 'Warning'

# Inventory contention
@Message like '%FOR UPDATE%' or @Message like '%concurrency%'

# Slow database queries (>100ms)
EfCoreQueryDuration > 100
```

### Key Metrics

| Metric | Description | Alert Threshold |
|---|---|---|
| `orders.created` | Orders placed per minute | N/A (baseline) |
| `orders.completed` | Orders fulfilled per minute | < 50% of created |
| `orders.failed` | Orders failed per minute | > 5% of created |
| `saga.manual_intervention` | Sagas needing admin | > 0 |
| `inventory.reservations.leaked` | Leaked reservations | > 0 |
| `payments.reconciliation.discrepancies` | Unresolved discrepancies | > 0 |
| `dlq.messages.count` | Dead letter queue depth | > 10 |
| `webhooks.failed` | Failed webhook processing | > 0 |

## Dead Letter Queue Management

Failed Wolverine messages are routed to the dead letter queue after retry exhaustion.

### Monitoring DLQ

```http
GET /api/admin/dlq?limit=50&offset=0
Authorization: Bearer <admin-elevated-token>
```

### Replaying Messages

```http
# Replay single message
POST /api/admin/dlq/{id}/replay
Authorization: Bearer <admin-elevated-token>

# Bulk replay by message type
POST /api/admin/dlq/bulk-replay
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "messageTypeFilter": "ProcessExternalPaymentConfirmation",
  "limit": 200
}
```

### Discarding Messages

```http
DELETE /api/admin/dlq/{id}
Authorization: Bearer <admin-elevated-token>
```

### DLQ Triage Procedure

1. **Check message type** — categorize as payment, inventory, or order
2. **Check failure reason** — transient (retry) vs permanent (investigate)
3. **For payment messages** — verify Stripe dashboard before replaying
4. **For inventory messages** — check stock levels before replaying
5. **Replay or discard** based on analysis
6. **Monitor** — verify replayed message processes successfully

## Order Recovery

### Stuck Saga Detection

```http
GET /api/v1/orders/manual-intervention
Authorization: Bearer <admin-token>
```

### Saga State Inspection

```http
GET /api/admin/orders/{orderId}/saga-details
Authorization: Bearer <admin-elevated-token>
```

Returns full saga state including:
- Current state (enum value)
- All tracking flags (inventory reserved, locked, paid, confirmed)
- Payment transaction ID
- Failure reason
- Timestamps

### Recovery Actions

#### Force Complete

Use when payment is confirmed in Stripe but saga is stuck:

```http
POST /api/admin/orders/{orderId}/force-complete
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "reason": "Payment confirmed in Stripe dashboard",
  "notes": "Charge ch_xxx settled, webhook delivery was delayed"
}
```

#### Override Payment Status

Use when webhook was lost but payment is verifiable:

```http
POST /api/admin/orders/{orderId}/override-payment-status
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "paymentStatus": "Succeeded",
  "stripeChargeId": "ch_xxx",
  "reason": "Manual Stripe dashboard verification"
}
```

#### Force Cancel

Use for unrecoverable orders:

```http
POST /api/admin/orders/{orderId}/force-cancel
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "reason": "Duplicate order, customer contacted",
  "refundAmount": 49.99,
  "notifyCustomer": true
}
```

#### Retry Step

Retry a specific saga step:

```http
POST /api/admin/orders/{orderId}/retry-step
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "step": "ProcessingPayment"
}
```

#### Bulk Retry

Retry all stuck sagas in a specific state:

```http
POST /api/admin/orders/bulk-retry-stuck
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "sagaState": "ProcessingPayment",
  "maxOrdersToRetry": 100
}
```

## Reconciliation

### Daily Reconciliation

The `ReconciliationEngine` runs T+1 daily reconciliation comparing internal payment records against the external PSP (Stripe) ledger.

#### Manual Trigger

```http
POST /api/admin/finance/reconciliation-sessions/trigger
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "date": "2025-01-15"
}
```

#### Viewing Results

```http
GET /api/admin/finance/reconciliation-sessions?startDate=2025-01-01&endDate=2025-01-31
Authorization: Bearer <admin-elevated-token>
```

### Discrepancy Types

| Type | Severity | Description | Action |
|---|---|---|---|
| `MissingExternal` | Warning | Internal record exists, no PSP record | Investigate Stripe dashboard |
| `MissingInternal` | **Critical** | PSP record exists, no internal record (GHOST CHARGE) | Immediate investigation |
| `AmountMismatch` | Warning | Amounts differ > $0.01 threshold | Review transaction details |

### Resolving Discrepancies

```http
POST /api/admin/finance/discrepancies/resolve
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "sessionId": "session-guid",
  "externalTxnId": "pi_xxx",
  "action": "AcceptDiscrepancy",
  "reason": "Timing difference, resolved on next day reconciliation"
}
```

**Resolution Actions:**

| Action | Description |
|---|---|
| `CreateShadowOrder` | Create an internal record for a ghost charge |
| `RefundGhostCharge` | Refund a charge with no internal record |
| `AcceptDiscrepancy` | Accept and document the discrepancy |
| `InvestigateFurther` | Flag for deeper investigation |

### Alerting

Ghost charges and amount mismatches above the configured threshold trigger `CriticalFinancialAlert`:

| Setting | Default | Description |
|---|---|---|
| `Finance:Alerting:DiscrepancyAlertThreshold` | `100` | Dollar threshold |
| `Finance:Alerting:SendEmailAlerts` | `false` | Email notifications |
| `Finance:Alerting:FinanceAlertEmail` | — | Alert recipient |
| `Finance:Alerting:PagerDutyRoutingKey` | — | PagerDuty integration |

## Reservation Cleanup

The `ReservationCleanupJob` runs as a `BackgroundService` with a `PeriodicTimer`:

| Setting | Default | Description |
|---|---|---|
| `ReservationCleanup:IntervalMinutes` | `5` | Cleanup cycle interval |
| `ReservationCleanup:ExpiryMinutes` | `30` | Reservation max age |

The job cleans:
1. **Active** reservations past their `ExpiresAt` timestamp
2. **PendingPayment** reservations that are stuck (payment timeout exceeded)

Released quantities return to the available stock pool.

## Database Maintenance

### Wolverine Table Monitoring

Monitor the size of Wolverine outbox tables:

```sql
-- Check outbox backlog
SELECT COUNT(*) FROM ordering.wolverine_outgoing_envelopes;

-- Check incoming queue
SELECT COUNT(*) FROM ordering.wolverine_incoming_envelopes;

-- Check saga states
SELECT state, COUNT(*)
FROM ordering.wolverine_saga_state
GROUP BY state;
```

A growing outbox backlog indicates message processing issues.

### Connection Pool

PostgreSQL connection pool settings are managed by Npgsql. Monitor for connection pool exhaustion:

```sql
-- Active connections
SELECT count(*) FROM pg_stat_activity WHERE datname = 'netcommerce';
```

### Index Maintenance

```sql
-- Reindex after heavy write operations
REINDEX TABLE catalog.products;
REINDEX TABLE inventory.stocks;
```

## Cache Management

### Redis Monitoring

```powershell
# Monitor Redis memory
redis-cli info memory

# Check key count
redis-cli dbsize

# Monitor slow commands
redis-cli slowlog get 10
```

### Cache Invalidation

Product cache is invalidated automatically via domain events. To force a full cache flush:

```powershell
redis-cli flushdb
```

HybridCache configuration:
- **L1 (in-process):** 5 minute TTL
- **L2 (Redis):** 60 minute TTL

## Search Index

### MeiliSearch Sync

Product search indexes are updated via domain event handlers when products are created, updated, or published.

To force a full reindex:

1. Delete the existing index via MeiliSearch API
2. The sync handler rebuilds the index on the next product change
3. Alternatively, trigger a full rebuild via the admin panel

### Search Index Health

```http
GET http://meilisearch:7700/health
```

## Incident Response

### Payment Webhook Failures

1. Check Stripe dashboard for delivery status
2. Search Seq logs: `@Message like 'Stripe webhook%' and @Level = 'Error'`
3. Check DLQ for failed payment commands
4. Verify `ProcessedWebhookEvents` table for idempotency issues
5. Replay from DLQ if the root cause is resolved

### Saga Stuck in ProcessingPayment

1. Check Stripe dashboard — is the PaymentIntent settled?
2. If yes: use `override-payment-status` endpoint
3. If no: wait for Stripe webhook retry (up to 72h)
4. If expired: use `force-cancel` with refund

### Ghost Charges Detected

1. Review reconciliation session details
2. Cross-reference with Stripe dashboard
3. If legitimate: create shadow order
4. If erroneous: refund ghost charge
5. Document resolution reason

### Database Connection Exhaustion

1. Check `pg_stat_activity` for stuck connections
2. Verify connection pool settings in Npgsql
3. Check for long-running transactions (saga timeouts)
4. Restart application pods if immediate relief needed
5. Investigate root cause (missing transaction scope closures)

## Related Documentation

- [Deployment](DEPLOYMENT.md) — infrastructure and deployment
- [Troubleshooting](TROUBLESHOOTING.md) — common issues and fixes
- [Financial Integrity](FINANCIAL_INTEGRITY_MATRIX.md) — reconciliation details
- [Webhook Reference](WEBHOOK_REFERENCE.md) — Stripe webhook handling
- [Messaging Patterns](MESSAGING_PATTERNS.md) — DLQ and outbox details
