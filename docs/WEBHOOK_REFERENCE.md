# Webhook Reference

Complete specification for the Stripe webhook integration in NetCommerce. The payment system follows a **webhook-first** pattern where payment state transitions are driven by Stripe webhook events, not by polling.

## Endpoint

```
POST /api/webhooks/stripe
```

| Property | Value |
|---|---|
| Authentication | AllowAnonymous (signature-verified) |
| Antiforgery | Disabled |
| Content Type | `application/json` (raw body) |
| Rate Limiting | None (Stripe controls delivery rate) |

## Signature Verification

Every webhook request is verified using the `Stripe-Signature` header and the webhook endpoint secret:

```csharp
var stripeEvent = EventUtility.ConstructEvent(
    json: requestBody,
    stripeSignatureHeader: request.Headers["Stripe-Signature"],
    secret: webhookSecret
);
```

**Failure Responses:**

| Condition | Response |
|---|---|
| Missing `Stripe-Signature` header | `400 Bad Request` — `"Missing signature"` |
| Invalid signature or payload | `400 Bad Request` — `"Invalid signature or payload"` |

## Idempotency

Webhook events are deduplicated using PostgreSQL-backed idempotency via `IWebhookEventStore`:

```sql
INSERT INTO processed_webhook_events (stripe_event_id, event_type, payment_intent_id, processed_at)
VALUES (@eventId, @type, @intentId, NOW())
ON CONFLICT (stripe_event_id) DO NOTHING;
```

| Scenario | Response |
|---|---|
| First delivery | `200 OK` — `{ "status": "processed", "eventId": "evt_..." }` |
| Duplicate delivery | `200 OK` — `{ "status": "duplicate", "eventId": "evt_..." }` |

Both responses return `200 OK` to prevent Stripe from retrying.

## Handled Event Types

### payment_intent.succeeded

Payment confirmed by Stripe. Triggers the saga to proceed from `ProcessingPayment` → `ConfirmingInventory`.

**Command Dispatched:** `ProcessExternalPaymentConfirmation`

```json
{
  "externalTransactionId": "pi_xxx",
  "status": "Succeeded",
  "stripeEventId": "evt_xxx"
}
```

**Saga Effect:** The `OrderFulfillmentSaga` receives `PaymentSucceeded` → sends `ConfirmInventoryCommand`.

---

### payment_intent.payment_failed

Payment rejected by Stripe. Triggers compensation: inventory release and order failure.

**Command Dispatched:** `ProcessExternalPaymentConfirmation`

```json
{
  "externalTransactionId": "pi_xxx",
  "status": "Failed",
  "stripeEventId": "evt_xxx"
}
```

**Saga Effect:** The saga receives `PaymentFailed` → sends `ReleaseInventoryReservationCommand` + `FailOrderCommand`.

---

### payment_intent.canceled

Payment cancelled (e.g., by customer or timeout). Same effect as payment failure.

**Command Dispatched:** `ProcessExternalPaymentConfirmation`

```json
{
  "externalTransactionId": "pi_xxx",
  "status": "Canceled",
  "stripeEventId": "evt_xxx"
}
```

---

### charge.refunded

Refund processed on a charge. Records the refund and updates the payment transaction.

**Command Dispatched:** `ProcessStripeRefundWebhook`

```json
{
  "chargeId": "ch_xxx",
  "refundId": "re_xxx",
  "amountRefunded": 4999,
  "totalRefundedSoFar": 4999,
  "currency": "gel",
  "stripeEventId": "evt_xxx",
  "paymentIntentId": "pi_xxx",
  "reason": "requested_by_customer"
}
```

**Downstream Effects:**
- `PartialRefundProcessed` event published
- Financial audit entry created
- If full refund: triggers `DisputeResolved` flow

---

### charge.dispute.created

Customer disputes a charge (chargeback). Creates a dispute record and begins evidence collection.

**Command Dispatched:** `ProcessStripeDisputeCreated`

```json
{
  "disputeId": "dp_xxx",
  "chargeId": "ch_xxx",
  "amount": 4999,
  "currency": "gel",
  "reason": "fraudulent",
  "status": "needs_response",
  "stripeEventId": "evt_xxx",
  "evidenceDueBy": "2025-02-15T00:00:00Z"
}
```

**Downstream Effects:**
- `DisputeCreatedForOrder` event published
- `CriticalFinancialAlert` published (disputes always trigger alerts)
- Financial audit entry created

---

### charge.dispute.updated / charge.dispute.closed

Dispute status change or resolution.

**Command Dispatched:** `ProcessStripeDisputeUpdated`

```json
{
  "disputeId": "dp_xxx",
  "chargeId": "ch_xxx",
  "status": "won",
  "stripeEventId": "evt_xxx"
}
```

**Dispute Outcomes (`DisputeOutcome` enum):**
- `Won` — dispute resolved in merchant's favor
- `Lost` — chargeback finalized (funds deducted)
- `ChargeRefunded` — merchant issued refund preemptively
- `WarningClosed` — inquiry closed without chargeback

---

### Unhandled Events

Any Stripe event type not listed above is logged at `Debug` level and acknowledged with `200 OK`. No command is dispatched.

## Response Formats

### Success (Processed)

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "processed",
  "eventId": "evt_1234567890"
}
```

### Success (Duplicate)

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "status": "duplicate",
  "eventId": "evt_1234567890"
}
```

### Signature Error

```http
HTTP/1.1 400 Bad Request

Missing signature
```

```http
HTTP/1.1 400 Bad Request

Invalid signature or payload
```

### Processing Error

```http
HTTP/1.1 500 Internal Server Error
Content-Type: application/problem+json

{
  "type": "https://netcommerce.example.com/errors/webhook-processing",
  "title": "Internal processing error",
  "status": 500,
  "detail": "Webhook will be retried by Stripe"
}
```

Returning `500` causes Stripe to retry with exponential backoff (up to 72 hours, ~15 attempts).

## Stripe Retry Behavior

Stripe retries failed webhook deliveries with exponential backoff:

| Attempt | Delay |
|---|---|
| 1 | Immediate |
| 2 | ~5 minutes |
| 3 | ~30 minutes |
| 4 | ~2 hours |
| 5 | ~5 hours |
| ... | Exponential up to ~72 hours |
| Final | ~72 hours total, ~15 attempts |

After exhausting retries, the event appears in the Stripe dashboard as failed delivery.

## Error Handling

Failed webhook processing follows this sequence:

1. Exception caught in handler
2. Event marked as failed via `IWebhookEventStore.MarkFailedAsync(eventId)`
3. `500` response returned to trigger Stripe retry
4. Retry attempt checks idempotency store — if event already claimed, skips reprocessing

## Configuration

| Setting | Location | Description |
|---|---|---|
| `Stripe:WebhookSecret` | `appsettings.json` / env var | Webhook endpoint signing secret |
| `Stripe:SecretKey` | `appsettings.json` / env var | Stripe API secret key |

### Stripe Resilience

Outbound Stripe API calls (not webhooks) use Polly resilience policies:

| Policy | Configuration |
|---|---|
| Retry | 3 attempts with exponential backoff |
| Circuit Breaker | Opens after consecutive failures, 30s break |

## Testing

Webhook processing is tested at multiple levels:

| Test | Scope | Description |
|---|---|---|
| `PaymentWebhookContractTests` | Unit | Tests each Stripe event type → command mapping |
| `PaymentWebhookTests` | Integration | Tests with real database and idempotency store |
| `WebhookRaceConditionTests` | Integration | Tests concurrent webhook delivery |
| `StripeWebhookDelayedDeliveryTests` | Integration | Tests late-arriving webhooks |

## Monitoring

Monitor webhook health via:

1. **Seq** — search for `Stripe webhook` in structured logs
2. **Dead Letter Queue** — failed payment commands appear in DLQ
3. **Reconciliation** — T+1 reconciliation detects missed webhooks (missing external records)
4. **Stripe Dashboard** — webhook delivery status and retry history

## Related Documentation

- [API Reference](API_REFERENCE.md) — all endpoints
- [Messaging Patterns](MESSAGING_PATTERNS.md) — saga integration
- [Financial Integrity](FINANCIAL_INTEGRITY_MATRIX.md) — reconciliation
- [Security](SECURITY.md) — webhook security considerations
- [Operations](OPERATIONS.md) — webhook monitoring procedures
