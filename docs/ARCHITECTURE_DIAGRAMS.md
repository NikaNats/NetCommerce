# Architecture Diagrams

## 1. Saga Failure Recovery - Guarded Compensation Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Order Fulfillment Saga Lifecycle                      │
└─────────────────────────────────────────────────────────────────────────┘

┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   NotStarted │────▶│  Reserving   │────▶│   Locking    │────▶│  Processing  │
│              │     │  Inventory   │     │  Inventory   │     │   Payment    │
└──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘
                            │                                           │
                            │ FAIL                                      │ SUCCESS
                            ▼                                           ▼
                     ┌──────────────┐                           ┌──────────────┐
                     │    Failed    │                           │ Confirming   │
                     │              │                           │  Inventory   │
                     └──────────────┘                           └──────────────┘
                                                                        │
                                                                        │ SUCCESS
                                                                        ▼
                                                                 ┌──────────────┐
                                                                 │  Completed   │
                                                                 │   (Saga      │
                                                                 │   Deleted)   │
                                                                 └──────────────┘

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                    THE GUARDED COMPENSATION PATTERN
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

When InventoryConfirmationFailed occurs (payment taken, inventory failed):

                           ┌──────────────┐
                           │ Confirming   │
                           │  Inventory   │
                           └──────┬───────┘
                                  │
                                  │ InventoryConfirmationFailed
                                  ▼
                          ┌───────────────┐
                          │ Compensating  │◀─── Issue RefundPaymentCommand
                          │   (WAITING)   │     Release inventory
                          └───────┬───────┘     Saga STAYS ALIVE
                                  │
                    ┌─────────────┴─────────────┐
                    │                           │
                    │ RefundCompleted           │ RefundFailed
                    ▼                           ▼
            ┌──────────────┐          ┌────────────────────┐
            │    Failed    │          │   Manual           │
            │   (Refund    │          │   Intervention     │
            │  Confirmed)  │          │    Required        │
            │              │          │   (Saga Persists   │
            │ MarkCompleted│          │    in Database)    │
            └──────────────┘          └────────────────────┘
                 Saga Deleted                Alert Triggered
                 Audit Trail                 Admin Dashboard
                 Complete                    Human Action
```

## 2. Shipping Module Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Bounded Context: Ordering                        │
└─────────────────────────────────────────────────────────────────────────┘
                                   │
                                   │ Order Finalized (Payment + Inventory OK)
                                   ▼
                    ┌──────────────────────────────┐
                    │  OrderReadyForShipping       │
                    │  Integration Event           │
                    │  ─────────────────────       │
                    │  - OrderId                   │
                    │  - Items (with weights)      │
                    │  - ShippingAddress           │
                    └──────────────┬───────────────┘
                                   │
                                   │ Published via Wolverine Outbox
                                   │ (Guaranteed Delivery)
                                   ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        Bounded Context: Shipping                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│   ┌─────────────────────────────────────────────┐                       │
│   │ OrderReadyForShippingHandler (Wolverine)     │                       │
│   └────────────────┬─────────────────────────────┘                       │
│                    │                                                     │
│                    │ Calls                                               │
│                    ▼                                                     │
│          ┌──────────────────┐                                           │
│          │ ShippingService  │                                           │
│          └────────┬─────────┘                                           │
│                   │                                                      │
│                   │ Select Courier                                      │
│                   ▼                                                     │
│    ┌──────────────────────────────┐                                    │
│    │   Courier Adapter Pattern    │                                    │
│    ├──────────────────────────────┤                                    │
│    │  ICourierAdapter Interface   │                                    │
│    │  ─────────────────────────   │                                    │
│    │  - CreateLabelAsync()        │                                    │
│    │  - CancelLabelAsync()        │                                    │
│    │  - GetTrackingStatusAsync()  │                                    │
│    └─────────┬────────────────────┘                                    │
│              │                                                          │
│     ┌────────┼──────────┬─────────┐                                   │
│     ▼        ▼          ▼         ▼                                   │
│  ┌─────┐ ┌──────┐  ┌──────┐  ┌──────┐                               │
│  │ DHL │ │FedEx │  │  UPS │  │ USPS │                               │
│  │     │ │      │  │      │  │      │                               │
│  └──┬──┘ └───┬──┘  └───┬──┘  └───┬──┘                               │
│     │        │         │         │                                    │
│     └────────┴─────────┴─────────┘                                   │
│              │ Courier APIs                                           │
│              ▼                                                        │
│     ┌─────────────────┐                                              │
│     │ CourierLabelResult│                                             │
│     │ - TrackingNumber │                                             │
│     │ - LabelUrl       │                                             │
│     │ - Cost           │                                             │
│     │ - ETA            │                                             │
│     └────────┬─────────┘                                             │
│              │                                                        │
│              │ Create Shipment Entity                                │
│              ▼                                                        │
│     ┌──────────────────────┐                                         │
│     │  Shipment Aggregate  │                                         │
│     │  ──────────────────  │                                         │
│     │  - TrackingNumber    │                                         │
│     │  - CourierProvider   │                                         │
│     │  - Weight/Dimensions │                                         │
│     │  - Status            │                                         │
│     └──────────┬───────────┘                                         │
│                │                                                      │
│                │ Raise Domain Event                                  │
│                ▼                                                      │
│    ┌─────────────────────────┐                                       │
│    │ShipmentCreatedIntegration│                                      │
│    │         Event            │                                      │
│    │  ───────────────────     │                                      │
│    │  - OrderId               │                                      │
│    │  - TrackingNumber        │                                      │
│    │  - CourierProvider       │                                      │
│    │  - ETA                   │                                      │
│    └────────────┬─────────────┘                                      │
│                 │                                                     │
└─────────────────┼─────────────────────────────────────────────────────┘
                  │
                  │ Published via Wolverine Outbox
                  ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        Bounded Context: Ordering                         │
│                                                                           │
│                ┌────────────────────────────┐                            │
│                │ ShipmentCreatedEvent       │                            │
│                │ Handler (future)           │                            │
│                └────────────┬───────────────┘                            │
│                             │                                            │
│                             ▼                                            │
│                  Update Order.Status = "Shipped"                         │
│                  Store TrackingNumber                                    │
│                  Notify Customer                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## 3. Courier Webhook Integration (Future)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        External Courier Systems                          │
└─────────────────────────────────────────────────────────────────────────┘
                                   │
                                   │ Webhooks (Status Updates)
                                   ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    Shipping.Infrastructure                               │
│                                                                           │
│   ┌─────────────────────────────────────────────────────────┐           │
│   │  WebhookController (REST API)                           │           │
│   │  ────────────────────────────────────                   │           │
│   │  POST /api/webhooks/dhl                                 │           │
│   │  POST /api/webhooks/fedex                               │           │
│   │  POST /api/webhooks/ups                                 │           │
│   └──────────────────────┬──────────────────────────────────┘           │
│                          │                                               │
│                          │ Parse & Validate                             │
│                          ▼                                               │
│              ┌─────────────────────┐                                    │
│              │  Shipment Repository │                                    │
│              │  - Get by TrackingNo │                                    │
│              │  - Update Status     │                                    │
│              └──────────┬───────────┘                                    │
│                         │                                                │
│                         │ Update Aggregate                              │
│                         ▼                                                │
│              ┌─────────────────────┐                                    │
│              │  Shipment.MarkPickedUp()                                 │
│              │  Shipment.MarkDelivered()                                │
│              │  Shipment.MarkFailed()                                   │
│              └──────────┬───────────┘                                    │
│                         │                                                │
│                         │ Raise Domain Event                            │
│                         ▼                                                │
│              ┌─────────────────────────┐                                │
│              │ShipmentDeliveredIntegration│                              │
│              │         Event            │                               │
│              └──────────┬──────────────┘                                │
└─────────────────────────┼────────────────────────────────────────────────┘
                          │
                          │ Published to Ordering
                          ▼
                Order.MarkDelivered()
                Customer Notification
```

## 4. Data Flow - Happy Path

```
Timeline:  ═══▶

┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│ Customer │────▶│ Ordering │────▶│ Inventory│────▶│ Payments │────▶│ Shipping │
│ Checkout │     │  Module  │     │  Module  │     │  Module  │     │  Module  │
└──────────┘     └──────────┘     └──────────┘     └──────────┘     └──────────┘
                      │                 │                 │                │
                      │                 │                 │                │
   1. Create Order    │                 │                 │                │
   ═══════════════════▶                 │                 │                │
                      │ 2. Reserve      │                 │                │
                      │    Inventory    │                 │                │
                      │ ════════════════▶                 │                │
                      │                 │                 │                │
                      │                 │ 3. Inventory    │                │
                      │                 │    Reserved     │                │
                      │ ◀════════════════                 │                │
                      │                 │                 │                │
                      │ 4. Request Payment                │                │
                      │ ══════════════════════════════════▶                │
                      │                 │                 │                │
                      │                 │                 │ 5. Payment     │
                      │                 │                 │    Succeeded   │
                      │ ◀══════════════════════════════════                │
                      │                 │                 │                │
                      │ 6. Confirm      │                 │                │
                      │    Inventory    │                 │                │
                      │ ════════════════▶                 │                │
                      │                 │                 │                │
                      │                 │ 7. Inventory    │                │
                      │                 │    Confirmed    │                │
                      │ ◀════════════════                 │                │
                      │                 │                 │                │
                      │ 8. Order Ready for Shipping       │                │
                      │ ═══════════════════════════════════════════════════▶
                      │                 │                 │                │
                      │                 │                 │                │ 9. Create
                      │                 │                 │                │    Shipping
                      │                 │                 │                │    Label
                      │                 │                 │                │    (DHL API)
                      │                 │                 │                │
                      │                 │                 │ 10. Shipment   │
                      │                 │                 │     Created    │
                      │ ◀═══════════════════════════════════════════════════
                      │                 │                 │                │
                      │ 11. Update Order Status to "Shipped"               │
                      │     Store Tracking Number                          │
                      │     Notify Customer                                 │
```

## 5. Failure Scenario - Inventory Confirmation Failed

```
┌──────────┐     ┌──────────┐     ┌──────────┐
│ Ordering │     │ Inventory│     │ Payments │
│  Saga    │     │  Module  │     │  Module  │
└────┬─────┘     └──────────┘     └──────────┘
     │                 │                 │
     │ ConfirmInventory│                 │
     │ ════════════════▶                 │
     │                 │                 │
     │                 │ ❌ Confirmation │
     │                 │    Failed!      │
     │ ◀════════════════                 │
     │                 │                 │
     │ [State: Compensating]             │
     │                 │                 │
     │ RefundPayment   │                 │
     │ ══════════════════════════════════▶
     │                 │                 │
     │ ReleaseInventory│                 │
     │ ════════════════▶                 │
     │                 │                 │
     │ [Saga WAITS for refund confirmation]
     │                 │                 │
     │                 │    ✅ Refund    │
     │                 │       Succeeded │
     │ ◀══════════════════════════════════
     │                 │                 │
     │ [State: Failed] │                 │
     │ [MarkCompleted()]                 │
     │ [Saga Deleted]  │                 │
     │                 │                 │


     Alternative Path:

     │                 │    ❌ Refund    │
     │                 │       Failed!   │
     │ ◀══════════════════════════════════
     │                 │                 │
     │ [State: ManualInterventionRequired]
     │ [Saga PERSISTS in Database]       │
     │ [Alert to Operations Team]        │
     │ [Shows up on Admin Dashboard]     │
```

---

## Key Architectural Decisions

### Why Bounded Contexts?
- **Autonomy**: Shipping can be scaled independently
- **Separation of Concerns**: Physical logistics != Digital ordering
- **Team Ownership**: Different teams can own different contexts
- **Technology Flexibility**: Each context can use optimal tech stack

### Why Adapter Pattern for Couriers?
- **Extensibility**: Add new couriers without changing core logic
- **Testability**: Mock adapters for unit tests
- **Resilience**: One courier failure doesn't affect others
- **Business Logic Isolation**: Shipping rules separate from API details

### Why Guarded Compensation?
- **Financial Integrity**: Never lose track of money
- **Compliance**: Full audit trail required by regulations
- **Observability**: Know exact state of every transaction
- **Human Oversight**: Critical failures escalate appropriately

---

*Diagrams created: 2026-01-05*
