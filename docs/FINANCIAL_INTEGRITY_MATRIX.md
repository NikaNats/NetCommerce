# Financial Integrity Matrix

Detailed documentation of the reconciliation engine, audit trail, discrepancy detection, and financial controls in NetCommerce.

## Overview

NetCommerce implements a **Triple-Lock Reconciliation** model to verify financial integrity:

1. **Internal Ledger** — payment records in the `payments` schema
2. **External PSP** — Stripe's transaction records via API
3. **Audit Log** — immutable, append-only `FinancialAuditEntry` records

The reconciliation engine compares these three sources daily to detect discrepancies, ghost charges, and data integrity issues.

## Reconciliation Engine

### Architecture

The `ReconciliationEngine` is the core service in `Finance.Application`:

```
ReconciliationEngine
├── IPaymentTransactionRepository  → Internal completed transactions
├── IPaymentGateway               → External PSP ledger (Stripe)
├── IReconciliationSessionRepository → Session persistence
├── IUnitOfWork                    → Transaction management
├── IMessageBus                    → Alert publishing (Wolverine)
└── AlertingOptions                → Threshold configuration
```

### T+1 Reconciliation Cycle

Reconciliation runs on a T+1 schedule — yesterday's transactions are reconciled today to account for PSP settlement delays.

**Daily flow:**

```
1. Fetch internal COMPLETED transactions for date
2. Fetch external PSP ledger for same date
3. Calculate gross totals (internal vs external)
4. Left-Outer-Join: internal → external (find MissingExternal)
5. Right-Outer-Join: external → internal (find GHOST CHARGES)
6. Save ReconciliationSession with discrepancies
7. Publish CriticalFinancialAlert for ghost charges & threshold breaches
```

### Comparison Algorithm

#### Left-Outer-Join (Internal → External)

For each internal completed transaction:

| Condition | Result |
|---|---|
| No `ExternalTransactionId` set | `MissingExternal` — possible system error |
| External ID set but no PSP match | `MissingExternal` — completed internally but absent from PSP |
| Match found, amount differs > $0.01 | `AmountMismatch` — with difference details |
| Match found, amounts agree | No discrepancy |

#### Right-Outer-Join (External → Internal)

For each external PSP transaction:

| Condition | Result |
|---|---|
| No matching internal record | **`MissingInternal` (GHOST CHARGE)** — customer charged but no order exists |
| Match found | No discrepancy |

Ghost charges are logged at `LogCritical` level and always trigger a `CriticalFinancialAlert`.

### Amount Comparison

Amounts are compared as **gross** values with a **$0.01 tolerance** for rounding differences:

```csharp
var amountDiff = Math.Abs(internalTxn.Amount.Amount - matchingExternal.Amount);
if (amountDiff > 0.01m) // 1 cent tolerance
```

## Discrepancy Types

| Type | Enum | Severity | Trigger |
|---|---|---|---|
| `MissingInternal` | `0` | **Critical** | PSP has charge, no internal record (ghost charge) |
| `MissingExternal` | `1` | Warning | Internal record exists, PSP has no match |
| `AmountMismatch` | `2` | Warning | Both exist but amounts differ > $0.01 |
| `FeeMismatch` | `3` | Info | PSP fees differ from expected |

### Discrepancy Value Object

```csharp
public record Discrepancy(
    string ExternalTxnId,
    DiscrepancyType Type,
    decimal Difference,
    string Reason,
    DateTime DetectedAt);
```

## Reconciliation Session

### Aggregate

`ReconciliationSession` is an aggregate root in the `finance` schema:

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Session identifier |
| `CalculatedForDate` | `DateTime` | Date being reconciled |
| `Status` | `ReconciliationStatus` | Session outcome |
| `TotalInternalAmount` | `decimal` | Sum of internal completed transactions |
| `TotalExternalAmount` | `decimal` | Sum of external PSP transactions |
| `Discrepancies` | `List<Discrepancy>` | All detected discrepancies |
| `StartedAt` | `DateTime` | Session start timestamp |
| `CompletedAt` | `DateTime?` | Session completion timestamp |
| `Notes` | `string?` | Resolution notes |

### Session States

| Status | Value | Description |
|---|---|---|
| `Started` | `0` | Session in progress |
| `Matched` | `1` | No discrepancies found |
| `Mismatched` | `2` | One or more discrepancies detected |
| `Failed` | `3` | Reconciliation failed (PSP API error, etc.) |

Status transitions:
- `Started` → `Matched` (no discrepancies at completion)
- `Started` → `Mismatched` (discrepancy added during processing)
- `Started` → `Failed` (exception during processing)

## Financial Audit Trail

### FinancialAuditEntry

An immutable, append-only audit log that records every financial state change:

**Design principles:**
- **Immutable** — no setters, no updates allowed
- **Append-only** — INSERT only, no UPDATE/DELETE at the database level
- **Complete** — captures before/after state for forensics
- **Server-timestamped** — `DateTime.UtcNow`, never client-provided

**Compliance coverage:**
- **SOX** — Financial transaction traceability
- **PCI-DSS** — Payment data access logging
- **GDPR** — Data processing records

### Audit Entry Fields

| Field | Type | Description |
|---|---|---|
| `AuditType` | `FinancialAuditType` | Category of financial operation |
| `EntityType` | `string` | Audited entity (Order, Payment, Refund) |
| `EntityId` | `string` | ID of audited entity |
| `ExternalTransactionId` | `string?` | Stripe PaymentIntent ID |
| `Amount` | `decimal?` | Monetary amount |
| `Currency` | `string?` | Currency code (GEL, USD, EUR) |
| `PreviousState` | `string?` | Before state (JSON) |
| `NewState` | `string?` | After state (JSON) |
| `ActorId` | `string` | User or system identifier |
| `ActorType` | `ActorType` | Actor category |
| `Description` | `string` | Human-readable action description |
| `Metadata` | `string?` | IP, User-Agent, correlation data |
| `OccurredAt` | `DateTime` | Server UTC timestamp |
| `CorrelationId` | `string?` | Distributed tracing correlation |

### Audit Types

| Category | Types |
|---|---|
| **Payment** | `PaymentInitiated` (0), `PaymentSucceeded` (1), `PaymentFailed` (2), `PaymentCaptured` (3) |
| **Refund** | `RefundInitiated` (10), `RefundSucceeded` (11), `RefundFailed` (12), `PartialRefund` (13) |
| **Dispute** | `DisputeCreated` (20), `DisputeUpdated` (21), `DisputeWon` (22), `DisputeLost` (23) |
| **Reconciliation** | `ReconciliationStarted` (30), `ReconciliationCompleted` (31), `ReconciliationFailed` (32), `DiscrepancyDetected` (33), `DiscrepancyResolved` (34) |
| **Manual** | `ManualAdjustment` (40) |
| **Ghost Charge** | `GhostChargeDetected` (50) |
| **Webhook** | `WebhookReceived` (60), `WebhookProcessed` (61) |
| **Alert** | `AlertTriggered` (70) |

### Actor Types

| Type | Value | Description |
|---|---|---|
| `User` | `0` | Human user action |
| `System` | `1` | Automated system process |
| `Webhook` | `2` | External webhook trigger |
| `Scheduler` | `3` | Scheduled job |
| `Admin` | `4` | Admin manual action |

### Audit Repository

`IFinancialAuditRepository` provides query capabilities:

| Method | Description |
|---|---|
| `AppendAsync(entry)` | Insert single audit entry |
| `AppendRangeAsync(entries)` | Batch insert entries |
| `GetByEntityAsync(entityType, entityId)` | Entries for a specific entity |
| `GetByDateRangeAsync(start, end, type?)` | Entries in date range, optionally filtered by type |
| `GetByExternalTransactionAsync(externalId)` | Entries for a Stripe transaction |
| `GetByCorrelationIdAsync(correlationId)` | Entries sharing a distributed trace |

## Alerting

### CriticalFinancialAlert

Published via Wolverine when critical discrepancies are detected:

```csharp
public record CriticalFinancialAlert(
    string ExternalTransactionId,
    decimal Amount,
    string Reason) : IDomainEvent;
```

### Alert Triggers

| Condition | Alert? |
|---|---|
| Ghost charge detected | **Always** |
| Amount mismatch ≥ threshold | Yes |
| Missing PSP record ≥ threshold | Yes |
| Amount mismatch < threshold | No |
| Missing PSP record < threshold | No |

### Configuration

```json
{
  "Finance": {
    "Alerting": {
      "DiscrepancyAlertThreshold": 100.00,
      "SendEmailAlerts": true,
      "FinanceAlertEmail": "finance-alerts@company.com",
      "PagerDutyRoutingKey": "your-pagerduty-routing-key"
    }
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `DiscrepancyAlertThreshold` | `$100.00` | Amount above which mismatches trigger alerts |
| `SendEmailAlerts` | `true` | Enable email notifications |
| `FinanceAlertEmail` | `finance-alerts@company.com` | Alert recipient |
| `PagerDutyRoutingKey` | — | PagerDuty Events API routing key |

## Discrepancy Resolution

### Resolution Actions

| Action | Value | Description |
|---|---|---|
| `CreateShadowOrder` | `0` | Create an internal record to account for the ghost charge |
| `RefundGhostCharge` | `1` | Issue immediate refund via PSP |
| `AcceptDiscrepancy` | `2` | Mark as accepted with documented reason |
| `InvestigateFurther` | `3` | Flag for deeper investigation |

### Resolution Command

```csharp
public record ResolveDiscrepancyCommand(
    Guid SessionId,
    string ExternalTxnId,
    DiscrepancyResolutionAction Action,
    string Reason,
    string ResolvedBy);
```

### Resolution API

```http
POST /api/admin/finance/discrepancies/resolve
Authorization: Bearer <admin-elevated-token>
Content-Type: application/json

{
  "sessionId": "...",
  "externalTxnId": "pi_xxx",
  "action": "RefundGhostCharge",
  "reason": "No matching order found after investigation"
}
```

## Payment Transaction Lifecycle

The audit trail captures the full payment lifecycle:

```
PaymentInitiated → PaymentSucceeded/PaymentFailed
    │                    │
    │                    ├── PaymentCaptured
    │                    │
    │                    └── RefundInitiated → RefundSucceeded/RefundFailed
    │
    └── WebhookReceived → WebhookProcessed
```

Each state transition creates a `FinancialAuditEntry` with `PreviousState` and `NewState` snapshots.

## Webhook Idempotency

Stripe delivers webhooks at-least-once. The `ProcessedWebhookEvent` entity deduplicates:

```sql
INSERT INTO finance.processed_webhook_events (event_id, event_type, processed_at)
VALUES (@eventId, @eventType, @processedAt)
ON CONFLICT (event_id) DO NOTHING;
```

If the insert succeeds (no conflict), the event is processed. If conflict is detected, the event was already processed and is skipped with a `200 OK` response.

## Integrity Verification Matrix

| Control | Mechanism | Frequency | Owner |
|---|---|---|---|
| Internal ↔ External match | Reconciliation Engine | Daily (T+1) | Automated |
| Ghost charge detection | Right-outer-join comparison | Daily | Automated + alert |
| Amount mismatch detection | Gross amount comparison | Daily | Automated + alert |
| Webhook idempotency | INSERT ON CONFLICT | Per webhook | Automated |
| Audit trail completeness | Append-only FinancialAuditEntry | Per financial event | Automated |
| Before/after state capture | JSON serialized PreviousState/NewState | Per state change | Automated |
| Manual resolution tracking | ResolveDiscrepancyCommand with audit | Ad hoc | Admin |
| Payment timeout detection | Saga timeout escalation | Per order | Automated |

## Database Schema

All finance data resides in the `finance` PostgreSQL schema:

| Table | Purpose |
|---|---|
| `finance.reconciliation_sessions` | Reconciliation session aggregates with discrepancies |
| `finance.financial_audit_log` | Immutable audit entries |
| `finance.processed_webhook_events` | Webhook deduplication |

All tables use soft-delete (where applicable) and `xmin` optimistic concurrency via `BaseDbContext`.

## Related Documentation

- [Operations](OPERATIONS.md) — reconciliation monitoring and procedures
- [Webhook Reference](WEBHOOK_REFERENCE.md) — Stripe webhook handling
- [Messaging Patterns](MESSAGING_PATTERNS.md) — saga payment flows
- [Security](SECURITY.md) — admin elevated authorization for finance endpoints
- [Domain Model](DOMAIN_MODEL.md) — financial domain entities
