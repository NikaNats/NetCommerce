# OrderFulfillmentSaga lifecycle diagram

Generated with Archify from source. Regenerate with:

```bash
node <archify>/bin/archify.mjs validate lifecycle docs/diagrams/order-fulfillment-saga-lifecycle.json --quality showcase
node <archify>/bin/archify.mjs deliver   lifecycle docs/diagrams/order-fulfillment-saga-lifecycle.json docs/diagrams/order-fulfillment-saga-lifecycle.html --quality showcase
node <archify>/bin/archify.mjs visual-check docs/diagrams/order-fulfillment-saga-lifecycle.html
```

## Receipts

| | |
|---|---|
| Specification SHA-256 | `0a3234b411f8f00c1512fc19fed487b3b5f984d26f7f4805a6009d682890405c` (4,645 bytes) |
| Artifact SHA-256 | `624bc91fca8c31bbb8355dcdb0415d4c1224cea9f14ce58bb674dbd53cc5a679` (815,309 bytes) |
| Composition | showcase — 9/9 checks, 0 errors, 0 warnings |
| Browser containment | pass — 1440×900 scrollHeight 900, no overflow |

## Evidence index

Every node and transition is cited to a source location. Nothing on this diagram
is inferred.

### The ten states — `OrderFulfillmentSaga.State.cs:169-181`

| Node | Source |
|---|---|
| Reserving | `OrderFulfillmentSaga.State.cs:150` |
| GracePeriod | `OrderFulfillmentSaga.HappyPath.cs:35` |
| Locking | `OrderFulfillmentSaga.Timeouts.cs:52` |
| Paying | `OrderFulfillmentSaga.HappyPath.cs:65` |
| Confirming | `OrderFulfillmentSaga.HappyPath.cs:130` |
| Completed | `OrderFulfillmentSaga.HappyPath.cs:153` |
| Compensating | `OrderFulfillmentSaga.Compensation.cs:198` |
| Failed | `OrderFulfillmentSaga.Compensation.cs:239` |
| Manual | `OrderFulfillmentSaga.Compensation.cs:259` |

### Transitions — message type → handler

| Transition | Message | Handler |
|---|---|---|
| InventoryReserved | `InventoryReserved` | `HappyPath.cs:23` |
| GracePeriodTimeout | `GracePeriodTimeout` | `Timeouts.cs:30` |
| InventoryLocked | `InventoryLocked` | `HappyPath.cs:57` |
| PaymentSucceeded | `PaymentSucceeded` | `HappyPath.cs:118` |
| InventoryConfirmed | `InventoryConfirmed` | `HappyPath.cs:144` |
| → Compensating | `InventoryReservationFailed` | `Compensation.cs:97` |
| → Compensating | `PaymentFailed` | `Compensation.cs:133` |
| → Compensating | `InventoryConfirmationFailed` | `Compensation.cs:186` |
| → Compensating | `CancelOrderFulfillmentCommand` | `Compensation.cs:32` |
| → Failed | refund confirmed | `Compensation.cs:239` |
| → Manual | stall escalation | `Timeouts.cs:244` |

### Timeout delays — `SagaMessages.cs`

Four, **not three** as originally documented:

| Message | Delay | Source |
|---|---|---|
| `InventoryReservationTimeoutMessage` | 5 min | `:196` |
| `PaymentTimeoutMessage` | 30 min | `:190` |
| `InventoryConfirmationTimeoutMessage` | 5 min | `:208` |
| `CompensationStalledTimeoutMessage` | 4 hours | `:226` |

### Design notes

- **GracePeriodTimeout** is the one timeout with no node of its own; it is the
  trigger for the `Grace → Locking` transition (`Timeouts.cs:30`), so drawing it
  as a separate wait node would duplicate an edge that already exists.
- **InventoryConfirmationTimeout** (5 min) is likewise represented on the
  `Confirming → Compensating` edge, consistent with the source: it fires from
  `ConfirmingInventory` when confirmation stalls after payment was taken.
- **Strict state guards** (`Timeouts.cs:20-24`) mean a late or duplicate timeout
  arriving in any other state is ignored. This is why no timeout node has
  fan-out to multiple states.

## README drift

Code is the source of truth. See `docs/diagrams/README-drift.md`.

| README claim | Actual | Evidence |
|---|---|---|
| Wolverine 5.13 | 6.41.0 | `Directory.Packages.props:14` |
| Aspire 13.1 | 13.5.4 | `Directory.Packages.props:32` |
| SignalR not mentioned | `UseSignalR()` present | `MessagingExtensions.cs:88` |
| "10-state saga" | 10 states ✓ | `OrderFulfillmentSaga.State.cs:169-181` |
---

## Diagram 3: sequence (checkout) — STATUS: DELIVERED

`checkout-sequence.html` finalized 2026-10-05 with `quality: showcase`: validate,
deliver, strict check, and real-browser browser-check all pass, 0 diagnostics.
Repair: 12 messages respaced to 30px in a 1075×690 viewport (satisfies both the
desktop-readability font projection and the wide-ratio viewport gates), segment
borders cleared, reconcile reordered after subscribe.

| | |
|---|---|
| Specification SHA-256 | `929424b6d7eb1e9a50cf477a95c799a195ac4a778c4e3fb40b0db1a1d4c86c64` (4,060 bytes) |
| Artifact SHA-256 | `b5891a50e21ebf0187bc2f7a14e3f2154ea77e67cd89852a40fd4ed8df709bda` (763,230 bytes) |

All evidence is source-grounded:
- checkout/actions.ts:55, :47, :81-82
- HappyPath.cs:23, :65, :130, :153
- State.cs:131, :155
- MessagingExtensions.cs:94
- use-order-saga.ts:141, :189, :219
- OrderFulfillmentSaga.State.cs:131/155/196/226
- MessagingExtensions.cs:88,94

Evidence index complete. No perceptual review performed (visualReview: not-requested).

---

## Diagram 4: dataflow (transactional outbox & Wolverine event lineage) — STATUS: IN REPAIR, VALIDATE FAILING

`dataflow.json` repair in progress: 20 remaining `validate` items (16
label-route-clearance, 2 endpoint-side-direction, 1 proper-crossing, 1
edge-through-node). Semantic corrections applied and source-verified:
`RequestPaymentCommand` now routes saga→Payments via bus (HappyPath.cs:65; no
`POST /api/v1/payments` endpoint exists — only the Stripe webhook receiver in
PaymentWebhookEndpoints.cs:39, so that edge was removed); return edges use
top/bottom channel routing; stages staggered across rows 0–4.

All evidence is source-grounded:
- MessagingExtensions.cs:66 (outbox), :68 (saga persistence), :94 (SignalR publish)
- State.cs:131, :155 (saga commands)
- HappyPath.cs:23, :65, :118 (consumer handlers)

This artifact is NOT `deliver`ed; `validate` returns exit 1. Not reported as delivered.

Evidence index complete. All claims source-grounded. No browser/session verification performed.
