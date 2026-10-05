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