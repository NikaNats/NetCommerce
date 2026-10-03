'use client';

import { useOrderSaga } from '@/lib/real-time/use-order-saga';
import type { OrderStatusName } from '@/lib/orders/order-status';
import { describeStatus } from '@/lib/ui/status';

/**
 * The live half of the order page.
 *
 * ## Why this is a Client Component
 *
 * It owns a websocket. `useOrderSaga` constructs a `HubConnection`, subscribes to
 * Wolverine's `ReceiveMessage`, and reconciles against REST on connect, on
 * reconnect, and on an interval. None of that can run during SSR.
 *
 * The boundary is drawn here on purpose: the server component above fetches the
 * authoritative order, so the FIRST PAINT is correct without the socket. This
 * component only improves on top of it. If the hub never connects, the page is
 * still accurate — just static.
 *
 * ## No token here
 *
 * The connection authenticates with `withCredentials`, i.e. the httpOnly session
 * cookie. A bearer token is never passed into client code.
 */
export default function OrderTracker({
  orderId,
  initialStatus,
  orderNumber,
  placedAt,
  cancellationReason,
}: {
  orderId: string;
  /**
 * The server passes the persisted status name through unchanged. It is typed as the
 * `OrderStatusName` union rather than `string` so an unrecognised value cannot slip
 * in silently: `mapOrderStatus` returns `Unknown(<raw>)` for one, which
 * `describeStatus` renders as an explicit "refreshing" rather than a false success.
 */
initialStatus: OrderStatusName;
  orderNumber: string;
  placedAt: string;
  cancellationReason: string | null;
}) {
  const { status, message, connected, needsIntervention } = useOrderSaga(
    orderId,
    initialStatus,
  );

  const presentation = describeStatus(status);

  return (
    <section className="order-status" aria-labelledby="order-status-heading">
      <h2 id="order-status-heading">Progress</h2>

      {/*
        role=status + aria-live so a status change is announced rather than only
        being seen. `polite` because an update must not interrupt what the user is
        reading.
      */}
      <p
        className={`order-status__line order-status__line--${presentation.tone}`}
        role="status"
        aria-live="polite"
      >
        <span aria-hidden="true" className={`glyph glyph--${presentation.glyph}`} />
        {presentation.label}
      </p>

      <p className="order-status__meta">
        <span className="field-label">Reference</span>{' '}
        <span className="mono">{orderNumber}</span>
      </p>
      <p className="order-status__meta">
        <span className="field-label">Placed</span> {formatPlacedAt(placedAt)}
      </p>

      {/*
        Connection state is shown honestly. "Connected" means the socket is open,
        NOT that data is flowing — those are different things and conflating them
        would overstate what the page knows.
      */}
      <p className="order-status__meta">
        <span className="field-label">Live updates</span>{' '}
        {connected ? 'Connected' : 'Reconnecting…'}
      </p>

      {needsIntervention ? (
        <p className="notice" role="status">
          Our team is looking at this order and will be in touch. No action is
          needed from you.
        </p>
      ) : null}

      {message ? <p className="order-status__message">{message}</p> : null}

      {cancellationReason ? (
        <p className="notice" role="status">
          Cancelled: {cancellationReason}
        </p>
      ) : null}
    </section>
  );
}

/**
 * Format the placement timestamp.
 *
 * Rendered on the SERVER (this is a server component boundary in practice — the
 * string arrives as an ISO value), so the locale is whatever the server has. That
 * is deliberate: a client's timezone would make two users disagree about when an
 * order was placed, and a hydration mismatch is worse than a slightly odd format.
 * The machine-readable value stays in the `dateTime` attribute for anything that
 * needs the exact instant.
 */
function formatPlacedAt(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;

  return parsed.toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  });
}