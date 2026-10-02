/**
 * Real-time order-status messages from the API's SignalR hub.
 *
 * ## Three vocabularies — do not mix them
 *
 * 1. `OrderStatus` (numeric enum, src/Ordering/Ordering.Domain/Orders/Order.cs:302-334)
 *    — the PERSISTED order state. Read from REST.
 * 2. `REALTIME_STATUS` (this file) — a free-form STRING emitted by the saga for
 *    UI push. Verified by grepping every `new OrderStatusChanged(...)` call site.
 * 3. The four strings in docs/MESSAGING_PATTERNS.md:238-245 — INCOMPLETE. The
 *    saga also emits "ManualInterventionRequired" (Timeouts.cs:250), which that
 *    doc omits.
 *
 * "StockSecured" and "ProcessingPayment" exist ONLY in vocabulary 2. They are
 * not OrderStatus members — Order.cs has StockConfirmed and AwaitingValidation
 * respectively. A realtime string must never be fed into mapOrderStatus().
 */

/** Every status string the saga actually emits, verified at each call site. */
export const REALTIME_STATUS = {
  StockSecured: 'StockSecured',
  ProcessingPayment: 'ProcessingPayment',
  Success: 'Success',
  Error: 'Error',
  ManualInterventionRequired: 'ManualInterventionRequired',
} as const;

export type RealtimeStatus = (typeof REALTIME_STATUS)[keyof typeof REALTIME_STATUS];

/** A status the server sent that this client does not know. */
export type UnknownRealtimeStatus = `Unknown(${string})`;
export type AnyRealtimeStatus = RealtimeStatus | UnknownRealtimeStatus;

export function isKnownRealtimeStatus(value: string): value is RealtimeStatus {
  return Object.values(REALTIME_STATUS).includes(value as RealtimeStatus);
}

/**
 * Surface an unrecognized status verbatim.
 *
 * "ManualInterventionRequired" was missing from the messaging doc, which is
 * exactly the kind of gap that makes guessing dangerous: a client that
 * defaulted unknown values to "Error" would tell a customer their order failed
 * when a human actually needs to intervene.
 */
export function mapRealtimeStatus(raw: string): AnyRealtimeStatus {
  return isKnownRealtimeStatus(raw) ? raw : `Unknown(${raw})`;
}

/** True when the saga is done and no further transitions are expected. */
export function isTerminalRealtimeStatus(status: AnyRealtimeStatus): boolean {
  return status === REALTIME_STATUS.Success || status === REALTIME_STATUS.Error;
}

/** True when a human must step in; the UI should stop auto-refreshing. */
export function needsManualIntervention(status: AnyRealtimeStatus): boolean {
  return status === REALTIME_STATUS.ManualInterventionRequired;
}

export interface OrderStatusChangedData {
  orderId: string;
  status: string;
  message: string;
}

/**
 * Wolverine's CloudEvents hub transport, as consumed by useOrderSaga.
 *
 * ## Why the client subscribes to `ReceiveMessage`, not `OrderStatusChanged`
 *
 * The hub at /api/messages is `Wolverine.SignalR.WolverineHub`, which exposes
 * exactly ONE public method: `ReceiveMessage(String json)`. It is not a
 * conventional hub with per-event methods, so
 *
 *   connection.on('OrderStatusChanged', ...)   // WRONG — never fires
 *   connection.on('ReceiveMessage', ...)       // RIGHT
 *
 * The payload is a CloudEvents envelope passed as a single JSON string whose
 * top-level keys are LOWERCASE (`specversion`, `datacontenttype`, `traceparent`)
 * while fields inside `data` are camelCase. `type` is the kebab-cased CLR name,
 * so OrderStatusChanged arrives as `order_status_changed`.
 *
 * With outgoing coalescing enabled the server instead invokes
 * `ReceiveCoalescedMessages`, whose `items` are STRINGS each holding a whole
 * CloudEvent document — hence the second parse below.
 *
 * ## Server side is wired
 *
 * `MessagingExtensions.cs` registers `opts.UseSignalR()` and a publish rule
 * `x.MessagesImplementing<IOrderNotification>().ToSignalR()`;
 * `IntegrationTestFixture` mirrors both, and `SignalRTransportWiringTests` pins
 * the routing by asking Wolverine's runtime where the message actually goes.
 */
export interface WolverineCloudEvent<T> {
  type: string;
  data: T;
  id: string;
  specversion: string;
  time: string;
  datacontenttype?: string;
  source?: string | null;
  traceparent?: string | null;
}

/** The CloudEvents type for OrderStatusChanged, derived from its CLR name. */
export const ORDER_STATUS_CHANGED_TYPE = 'order_status_changed';

/** Payload when Wolverine's outgoing coalescing is enabled. */
export interface CoalescedBatch {
  wolverineBatch: boolean;
  /** Each item is a full CloudEvent document encoded as a STRING. */
  items: string[];
}

/**
 * Parse one CloudEvent string from the hub.
 *
 * Returns null for anything that is not a usable envelope so a malformed frame
 * degrades to "no update" instead of throwing inside a SignalR handler.
 */
export function parseCloudEvent(
  json: string,
): WolverineCloudEvent<OrderStatusChangedData> | null {
  try {
    const parsed = JSON.parse(json) as WolverineCloudEvent<OrderStatusChangedData>;
    if (!parsed || typeof parsed.type !== 'string' || parsed.data == null) return null;
    return parsed;
  } catch {
    return null;
  }
}

/**
 * Parse a coalesced batch frame.
 *
 * `items` are strings, each a whole CloudEvent document, so every item needs its
 * own JSON.parse. A double-encoded frame is the expected shape here, not a bug.
 */
export function parseCoalescedBatch(
  json: string,
): WolverineCloudEvent<OrderStatusChangedData>[] {
  let batch: CoalescedBatch;
  try {
    batch = JSON.parse(json) as CoalescedBatch;
  } catch {
    return [];
  }

  if (!batch?.wolverineBatch || !Array.isArray(batch.items)) return [];

  return batch.items
    .map((item) => parseCloudEvent(item))
    .filter((e): e is WolverineCloudEvent<OrderStatusChangedData> => e !== null);
}