# Financial Integrity Matrix

## Executive Summary

**Document Version:** 1.0
**Last Updated:** February 2026
**Author:** NetCommerce Engineering

This document defines the Financial Integrity Matrix - a comprehensive framework ensuring every financial transaction in NetCommerce maintains consistency across all system boundaries. The matrix maps money flows through the Order Fulfillment Saga and identifies integrity checkpoints that prevent financial discrepancies.

## 1. Core Principle: Double-Entry Verification

Every financial state change must be verifiable through **two independent sources**:

| Action | Internal Source | External Source | Verification Method |
|--------|-----------------|-----------------|---------------------|
| Payment Initiated | `PaymentTransaction.Status = Pending` | Stripe PaymentIntent created | IdempotencyKey correlation |
| Payment Completed | Webhook → `PaymentTransaction.Status = Completed` | Stripe `payment_intent.succeeded` | ExternalTransactionId match |
| Refund Issued | `PaymentTransaction.Status = Refunded` | Stripe `charge.refunded` | RefundId correlation |
| Settlement | Reconciliation Session | Stripe Balance API | T+1 daily reconciliation |

## 2. Money State Machine

```
                                    ORDER LIFECYCLE
┌──────────────────────────────────────────────────────────────────────────────┐
│                                                                               │
│   ┌─────────────┐         ┌─────────────┐         ┌─────────────┐           │
│   │   Customer  │ ──$──▶  │   Stripe    │ ──$──▶  │  Merchant   │           │
│   │   Account   │         │   (Escrow)  │         │   Account   │           │
│   └─────────────┘         └─────────────┘         └─────────────┘           │
│          │                       │                       │                   │
│          │                       │                       │                   │
│   T=0: Payment                   │                       │                   │
│   Intent Created                 │                       │                   │
│          │                       │                       │                   │
│          ▼                       ▼                       │                   │
│   ┌──────────────────────────────────────┐              │                   │
│   │  INTERNAL STATE: Pending             │              │                   │
│   │  EXTERNAL STATE: requires_payment    │              │                   │
│   │  💰 MONEY LOCATION: Customer Card    │              │                   │
│   └──────────────────────────────────────┘              │                   │
│          │                                               │                   │
│          │ T=1: Webhook: payment_intent.succeeded       │                   │
│          ▼                                               │                   │
│   ┌──────────────────────────────────────┐              │                   │
│   │  INTERNAL STATE: Completed           │              │                   │
│   │  EXTERNAL STATE: succeeded           │              │                   │
│   │  💰 MONEY LOCATION: Stripe Balance   │◀─────────────┘                   │
│   └──────────────────────────────────────┘                                   │
│          │                                                                   │
│          │ T+2 days: Stripe Payout                                          │
│          ▼                                                                   │
│   ┌──────────────────────────────────────┐                                   │
│   │  💰 MONEY LOCATION: Merchant Bank    │                                   │
│   └──────────────────────────────────────┘                                   │
│                                                                               │
└──────────────────────────────────────────────────────────────────────────────┘
```

## 3. Financial Integrity Checkpoints

### 3.1 Pre-Payment Checkpoint (T=0)

**Location:** `SagaPaymentHandlers.Handle(RequestPaymentCommand)`

| Check | Implementation | Failure Response |
|-------|---------------|------------------|
| Idempotency | `IdempotencyKey = payment_{messageId}` | Return existing result |
| Amount validation | `Money.Amount > 0` | Reject with validation error |
| Order exists | Saga state verification | Saga not found error |
| Internal ledger entry | `PaymentTransaction.Create()` | Transaction rollback |

```csharp
// Critical: Create internal record BEFORE calling external PSP
var paymentTransaction = PaymentTransaction.Create(
    orderId: command.OrderId,
    amount: command.Amount,
    provider: paymentGateway.Provider,
    idempotencyKey: $"payment_{envelope.Id}");

await repository.AddAsync(paymentTransaction);
```

### 3.2 Payment Processing Checkpoint (T=1)

**Location:** `StripePaymentGateway.ProcessPaymentAsync`

| Check | Implementation | Failure Response |
|-------|---------------|------------------|
| Idempotency Key sent | Stripe `IdempotencyKey` header | Stripe returns cached response |
| Response captured | `ExternalTransactionId` stored | Retry with same key |
| Status mapping | `Pending` always returned | Webhook-first pattern enforced |

**CRITICAL RULE:** `ProcessPaymentAsync` MUST return `Pending` status, never `Succeeded`:

```csharp
// Gateway returns Pending - final confirmation comes from webhook
return Result.Success(new PaymentResult(
    Status: PaymentResultStatus.Pending,  // NEVER Succeeded here
    ExternalTransactionId: paymentIntent.Id,
    ...));
```

### 3.3 Webhook Confirmation Checkpoint (T=2)

**Location:** `PaymentWebhookController` → `ProcessExternalPaymentConfirmationHandler`

| Check | Implementation | Failure Response |
|-------|---------------|------------------|
| Signature verification | `Stripe.Webhook.ConstructEvent()` | 400 Bad Request |
| Duplicate detection | Check `PaymentTransaction` status | Idempotent success |
| Event correlation | Match `ExternalTransactionId` | Create orphan alert |
| State transition | `PaymentTransaction.MarkAsCompleted()` | Log discrepancy |

**Ghost Charge Detection:**
```csharp
var transaction = await repository.GetByExternalIdAsync(externalTxnId);
if (transaction is null)
{
    // CRITICAL: Money was taken but no order exists!
    await alertService.RaiseGhostChargeAlert(externalTxnId, amount);
    // DO NOT return error - Stripe will retry
    return Ok();
}
```

### 3.4 Daily Reconciliation Checkpoint (T+1 Day)

**Location:** `ReconciliationEngine.ReconcileDailyAsync`

| Check | Type | Severity | Auto-Action |
|-------|------|----------|-------------|
| Missing External | Our record exists, PSP missing | HIGH | Manual review |
| Missing Internal (Ghost) | PSP charged, we have no record | CRITICAL | Immediate alert |
| Amount Mismatch | Values differ > $0.01 | MEDIUM | Flag for review |
| Currency Mismatch | Different currencies | HIGH | Block settlement |

**Triple-Lock Verification:**
```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Internal       │     │  External       │     │  Audit Log      │
│  Ledger         │     │  PSP API        │     │  (Immutable)    │
│  (PostgreSQL)   │ ══▶ │  (Stripe)       │ ══▶ │  (Append-only)  │
└─────────────────┘     └─────────────────┘     └─────────────────┘
       │                        │                        │
       └────────────────────────┴────────────────────────┘
                          │
                   All three must agree
```

## 4. Saga State → Financial State Matrix

| Saga State | Inventory Status | Payment Status | Money Location | Recovery Action |
|------------|------------------|----------------|----------------|-----------------|
| `NotStarted` | None | None | Customer | N/A |
| `ReservingInventory` | Pending | None | Customer | Auto-release timeout |
| `InGracePeriod` | Reserved | None | Customer | User cancel allowed |
| `LockingInventory` | Locked | None | Customer | Auto-release timeout |
| `ProcessingPayment` | Locked | Pending | In Transit | Wait for webhook |
| `ConfirmingInventory` | Locked | Completed | Stripe | Refund if confirm fails |
| `Compensating` | Release pending | Refund pending | In Transit | Manual if stuck |
| `Completed` | Confirmed | Completed | Stripe → Merchant | N/A |
| `Failed` | Released | Refunded/None | Customer | Audit trail only |
| `ManualInterventionRequired` | Unknown | Unknown | Unknown | Human review |

## 5. Compensation Flow Integrity

### 5.1 Guarded Compensation Pattern

When `InventoryConfirmationFailed` occurs AFTER payment succeeds:

```
┌─────────────────────────────────────────────────────────────────────┐
│                    COMPENSATION INTEGRITY FLOW                       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  InventoryConfirmationFailed                                         │
│         │                                                            │
│         ▼                                                            │
│  ┌──────────────────┐                                               │
│  │ State =          │                                               │
│  │ Compensating     │◀──── Saga STAYS ALIVE until refund confirmed │
│  └────────┬─────────┘                                               │
│           │                                                          │
│           │ Issue RefundPaymentCommand                              │
│           ▼                                                          │
│  ┌──────────────────┐                                               │
│  │ Wait for         │                                               │
│  │ RefundCompleted  │                                               │
│  │ or RefundFailed  │                                               │
│  └────────┬─────────┘                                               │
│           │                                                          │
│     ┌─────┴─────┐                                                   │
│     │           │                                                    │
│     ▼           ▼                                                    │
│  RefundOK    RefundFailed                                           │
│     │           │                                                    │
│     ▼           ▼                                                    │
│  ┌──────┐   ┌──────────────────┐                                    │
│  │Failed│   │ManualIntervention│◀── MONEY AT RISK - ALERT          │
│  │      │   │Required          │                                    │
│  └──────┘   └──────────────────┘                                    │
│     │                │                                               │
│     ▼                ▼                                               │
│  Saga deleted    Saga persists                                      │
│  Audit complete  Admin dashboard                                    │
│                  Human action                                        │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.2 Refund Integrity Checks

| Check | Timing | Implementation |
|-------|--------|---------------|
| Refund amount = original | Before refund | `RefundPaymentCommand.Amount == Saga.TotalAmount` |
| ExternalTransactionId exists | Before refund | Verify PaymentTransactionId not null |
| Stripe refund created | After call | Capture `Refund.Id` |
| Refund webhook received | T+seconds | Match RefundId, update status |
| Reconciliation verification | T+1 day | Compare net amounts |

## 6. Observability & Alerting Matrix

### 6.1 Financial Metrics (Grafana Dashboard)

| Metric | Type | Alert Threshold | Runbook |
|--------|------|-----------------|---------|
| `payments.ghost_charges.count` | Counter | > 0 | RUNBOOK-FIN-001 |
| `payments.refund.pending.duration` | Histogram | p95 > 5min | RUNBOOK-FIN-002 |
| `reconciliation.discrepancy.amount` | Gauge | > $100 | RUNBOOK-FIN-003 |
| `saga.manual_intervention.count` | Counter | > 0 | RUNBOOK-FIN-004 |
| `payments.amount.processing` | Gauge | - | Dashboard only |

### 6.2 Alert Severity Levels

| Level | Condition | Response Time | Example |
|-------|-----------|---------------|---------|
| P1 CRITICAL | Money at risk, no automated recovery | 15 min | Ghost charge detected |
| P2 HIGH | Money at risk, automated recovery in progress | 1 hour | Refund stuck in pending |
| P3 MEDIUM | Potential discrepancy, reconciliation flagged | 24 hours | Amount mismatch < $10 |
| P4 LOW | Informational, audit trail only | Next business day | Duplicate webhook |

## 7. Financial Audit Trail

Every money movement creates an immutable audit entry:

```csharp
public record FinancialAuditEntry
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string EventType { get; init; }  // "PaymentInitiated", "PaymentCompleted", "RefundIssued"
    public decimal Amount { get; init; }
    public string Currency { get; init; }
    public string? ExternalTransactionId { get; init; }
    public string? IdempotencyKey { get; init; }
    public DateTime Timestamp { get; init; }
    public string Source { get; init; }  // "Saga", "Webhook", "Reconciliation", "Manual"
}
```

**Retention Policy:** 7 years (regulatory compliance)

## 8. Implementation Checklist

### Before Production Deployment

- [ ] Verify webhook signature validation is enabled
- [ ] Confirm idempotency keys are unique per operation
- [ ] Test ghost charge detection with simulated orphan webhooks
- [ ] Validate reconciliation job runs at T+1 04:00 UTC
- [ ] Confirm alert routing to PagerDuty/Opsgenie
- [ ] Review ManualInterventionRequired saga handling runbook
- [ ] Test refund flow end-to-end with real Stripe test mode
- [ ] Verify audit log retention policy is configured

### Monitoring Setup

- [ ] Grafana dashboard: `Financial Integrity Overview`
- [ ] Alert rule: Ghost charge detection
- [ ] Alert rule: Reconciliation discrepancy > threshold
- [ ] Alert rule: Saga stuck in Compensating > 10 minutes
- [ ] Daily report: Reconciliation summary email

## 9. Related Documentation

- [ARCHITECTURE_DIAGRAMS.md](./ARCHITECTURE_DIAGRAMS.md) - Saga flow diagrams
- [STRONG_RESERVATION_PATTERN.md](./STRONG_RESERVATION_PATTERN.md) - Inventory reservation before payment
- [PII_VAULT_ARCHITECTURE.md](./PII_VAULT_ARCHITECTURE.md) - Customer data handling

## Appendix A: Discrepancy Type Reference

| Type | Code | Description | Auto-Resolution |
|------|------|-------------|-----------------|
| `MissingExternal` | FIN-001 | Our record exists, PSP has no matching transaction | Manual review |
| `MissingInternal` | FIN-002 | PSP charged customer, we have no order (GHOST) | Immediate refund |
| `AmountMismatch` | FIN-003 | Transaction amounts differ between systems | Manual review |
| `CurrencyMismatch` | FIN-004 | Currency codes don't match | Block + alert |
| `DuplicateCharge` | FIN-005 | Multiple charges for same IdempotencyKey | Refund duplicate |
| `RefundNotApplied` | FIN-006 | Refund issued but not reflected in reconciliation | Escalate to PSP |

## Appendix B: Money Flow Test Scenarios

| Scenario | Expected Outcome | Test Method |
|----------|------------------|-------------|
| Happy path | Order completed, settlement T+2 | Integration test |
| Payment declined | Inventory released, no charge | Integration test |
| Webhook timeout | Retry mechanism, eventual consistency | Chaos test |
| PSP outage | Circuit breaker, graceful degradation | Load test |
| Ghost charge simulation | Alert fired, refund issued | Manual test |
| Duplicate webhook | Idempotent handling, single state change | Integration test |
| Refund failure | ManualInterventionRequired state | Integration test |
