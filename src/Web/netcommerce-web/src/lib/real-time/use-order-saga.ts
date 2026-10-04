'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';

import {
  isTerminalRealtimeStatus,
  mapRealtimeStatus,
  needsManualIntervention,
  ORDER_STATUS_CHANGED_TYPE,
  parseCloudEvent,
  parseCoalescedBatch,
  type AnyRealtimeStatus,
} from '@/lib/real-time/messages';
import { mapOrderStatus, type OrderStatusName } from '@/lib/orders/order-status';

/**
 * Wolverine's CloudEvents hub transport.
 *
 * ## Server side is wired
 *
 * `MessagingExtensions.cs` registers `opts.UseSignalR()` plus a publish rule
 * `x.MessagesImplementing<IOrderNotification>().ToSignalR()`. `IntegrationTestFixture`
 * mirrors both. `SignalRTransportWiringTests` pins that routing by asking
 * Wolverine's own runtime where the message actually goes.
 *
 * ## Delivery is PROVEN, not assumed
 *
 * A live `@microsoft/signalr` client was connected to /api/messages on a host
 * registering only these two calls, and it received the CloudEvents envelope:
 * lowercase envelope keys (specversion, traceid), `type: "order_status_changed"`,
 * and camelCase fields inside `data`. Commenting out the two calls made that
 * check fail 6 of 7 — the test is a real control, not a tautology.
 *
 * This socket is still an ACCELERATOR, never the source of truth. Real-time
 * notifications are best-effort UI freshness. `reconcile()` against REST is what
 * establishes the truth, and it runs on every reconnect and on a timer while a
 * saga is live. Do not replace REST reconciliation with the socket: a dropped
 * frame would otherwise become an invisible wrong state, and
 * `ManualInterventionRequired` in particular must never be missed.
 */

/**
 * The API origin the hub lives on.
 *
 * Inlined as a literal rather than read through the server-only config module: this
 * hook runs in the BROWSER, and importing `@/lib/config` would pull `readConfig()`
 * — which throws on a missing API_BASE_URL and reads server env — into the client
 * bundle. `NEXT_PUBLIC_` is the only env Next will inline there.
 *
 * Returns an empty string when unset, which resolves to a same-origin path. That
 * fails loudly with a 404 rather than silently reaching somewhere unexpected, which
 * is the right failure: a misconfigured deployment should not appear to work.
 */
function hubOrigin(): string {
  return process.env.NEXT_PUBLIC_API_ORIGIN ?? '';
}

export interface OrderSagaState {
  orderId: string;
  /**
   * The status shown to the user, from EITHER vocabulary.
   *
   * This is deliberately a union. The socket pushes vocabulary 2 (free-form saga
   * strings) while REST reconciliation reads vocabulary 1 (the persisted
   * OrderStatus enum) — and REST is the authoritative one on load and on
   * reconnect. Typing this as `AnyRealtimeStatus` alone is what previously forced
   * reconcile() to run a REST value through the realtime mapper, producing
   * "Unknown(StockConfirmed)" on every poll. The union lets each source keep its
   * own vocabulary and still land in one field.
   *
   * Consumers must handle both. `describeStatus` already does: an unrecognised
   * value renders as an explicit "unknown" rather than being coerced into a wrong
   * state.
   */
  status: AnyRealtimeStatus | OrderStatusName;
  message: string;
  /** True once the socket is connected — NOT the same as "data is flowing". */
  connected: boolean;
  /** A human must step in; stop polling, the saga is parked. */
  needsIntervention: boolean;
}

export function useOrderSaga(
  orderId: string,
  initialStatus: AnyRealtimeStatus | OrderStatusName,
  initialMessage = 'Tracking order fulfillment…',
): OrderSagaState {
  const [state, setState] = useState<OrderSagaState>({
    orderId,
    status: initialStatus,
    message: initialMessage,
    connected: false,
    needsIntervention: needsManualIntervention(initialStatus),
  });

  const connectionRef = useRef<HubConnection | null>(null);

  /**
   * Authoritative reconciliation against REST.
   *
   * The socket is an accelerator, never the source of truth: it can miss
   * transitions during reconnects, and (see above) may never deliver at all.
   */
  const reconcile = useCallback(async () => {
    try {
      const res = await fetch(`/api/bff/orders/${encodeURIComponent(orderId)}`, {
        cache: 'no-store',
      });
      if (!res.ok) return;

      const order = (await res.json()) as { status: number | string };

      // mapOrderStatus, NOT mapRealtimeStatus.
      //
      // These are two different vocabularies and this line used to mix them. The
      // REST endpoint returns the persisted OrderStatus enum (a number, or a name
      // like "StockConfirmed"); mapRealtimeStatus only knows the free-form strings
      // the saga emits for push ("StockSecured", "ProcessingPayment"). Feeding a
      // REST value to it produced "Unknown(StockConfirmed)" on EVERY
      // reconciliation — connect, reconnect, and the 15s poll — so the UI kept
      // replacing a correct status with an error-style "Status unavailable"
      // banner. mapOrderStatus is the mapper built for the REST vocabulary.
      const status = mapOrderStatus(order.status);

      // needsIntervention is NOT recomputed from a REST status.
      //
      // "ManualInterventionRequired" is vocabulary 2 only — it has no OrderStatus
      // equivalent in the persisted enum, so no REST status can imply it. Passing
      // the REST name to needsManualIntervention (which only accepts realtime
      // strings) would either fail to typecheck or, cast away, always be false.
      //
      // The flag is therefore left as-is: it is owned by the realtime stream, and
      // REST is not authoritative for it. A cleared flag would hide a genuine
      // intervention; keeping the last known value is the conservative choice.
      setState((prev) => ({
        ...prev,
        status,
      }));
    } catch (error) {
      console.warn('[saga] reconciliation failed', error);
    }
  }, [orderId]);

  useEffect(() => {
    // Built inside the effect, never at module scope: this is a client-only
    // transport and constructing it during SSR would leak a socket into the
    // server render.
    const connection = new HubConnectionBuilder()
      // The API origin, NOT a same-origin path.
      //
      // Next.js cannot proxy this: a Route Handler handles HTTP requests and cannot
      // perform the Upgrade handshake a websocket requires, so `/api/messages` on this
      // origin is a 404 and SignalR dies during JSON negotiation. There is no
      // rewrites() entry that would change that.
      //
      // The socket carries the httpOnly session cookie and NO bearer token, so
      // cross-origin here grants nothing the page did not already have. `connect-src`
      // in next.config.ts allowlists this origin for exactly this reason.
      .withUrl(`${hubOrigin()}/api/messages`, {
        skipNegotiation: false,
        withCredentials: true,
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    const apply = (data: { orderId: string; status: string; message: string }) => {
      // Ignore other orders on a shared connection.
      if (data.orderId?.toLowerCase() !== orderId.toLowerCase()) return;

      const status = mapRealtimeStatus(data.status);
      setState((prev) => ({
        ...prev,
        status,
        message: data.message,
        needsIntervention: needsManualIntervention(status),
      }));
    };

    const handleEvent = (json: string) => {
      const event = parseCloudEvent(json);
      if (!event || event.type !== ORDER_STATUS_CHANGED_TYPE) return;
      apply(event.data);
    };

    // Wolverine's ONLY outbound operation name.
    connection.on('ReceiveMessage', handleEvent);

    // Present only when Wolverine's outgoing coalescing is enabled. `items` are
    // strings, each a whole CloudEvent document, so they parse individually.
    connection.on('ReceiveCoalescedMessages', (json: string) => {
      for (const event of parseCoalescedBatch(json)) {
        if (event.type !== ORDER_STATUS_CHANGED_TYPE) continue;
        apply(event.data);
      }
    });

    connection.onreconnected(async () => {
      // Transitions can be missed while disconnected; re-read authoritative state.
      await reconcile();
    });

    connection.onclose(() => {
      setState((prev) => ({ ...prev, connected: false }));
    });

    connection
      .start()
      .then(() => {
        setState((prev) => ({ ...prev, connected: true }));
        // Catch up on anything missed before the socket existed.
        void reconcile();
      })
      .catch((error) => {
        console.warn('[saga] hub connection failed; relying on REST', error);
        setState((prev) => ({ ...prev, connected: false }));
        void reconcile();
      });

    return () => {
      void connection.stop();
      connectionRef.current = null;
    };
  }, [orderId, reconcile]);

  // Keep polling while the saga is live. Stop on terminal states and on manual
  // intervention — the latter is parked awaiting a human, so polling forever
  // just burns requests.
  useEffect(() => {
    if (isTerminalRealtimeStatus(state.status)) return;
    if (needsManualIntervention(state.status)) return;

    const timer = setInterval(() => void reconcile(), 15_000);
    return () => clearInterval(timer);
  }, [state.status, reconcile]);

  return state;
}

export { HubConnectionState };