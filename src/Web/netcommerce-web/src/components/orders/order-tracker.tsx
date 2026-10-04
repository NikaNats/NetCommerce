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
export interface OrderTimeline {
  paidAt: string | null;
  shippedAt: string | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
}

export default function OrderTracker({
  orderId,
  initialStatus,
  orderNumber,
  placedAt,
  timeline,
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
  /**
   * Milestone timestamps from the OrderResponse projection, in domain order.
   * Each is null until the transition happens — a milestone renders only when
   * its instant exists, so the timeline can never claim an event that has not
   * occurred. The socket carries no timestamps, so this prop does not update
   * live; the 15s REST reconciliation re-reads it.
   */
  timeline: OrderTimeline;
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
        <span className="field-label">Placed</span> {formatInstant(placedAt)}
      </p>

      <OrderTimeline milestones={timeline} />

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
 * Milestone timeline from the persisted projection.
 *
 * Domain order, each rendered only when its instant exists: Paid → Shipped →
 * Delivered, or Cancelled instead. Placed is shown above and never repeated
 * here.
 */
function OrderTimeline({ milestones }: { milestones: OrderTimeline }) {
  const events = [
    { label: 'Payment received', at: milestones.paidAt },
    { label: 'Shipped', at: milestones.shippedAt },
    { label: 'Delivered', at: milestones.deliveredAt },
    { label: 'Cancelled', at: milestones.cancelledAt },
  ].filter((event): event is { label: string; at: string } => event.at !== null);

  if (events.length === 0) return null;

  return (
    <ol className="order-timeline">
      {events.map((event) => (
        <li key={event.label} className="order-status__meta">
          <span className="field-label">{event.label}</span>{' '}
          <time dateTime={event.at}>{formatInstant(event.at)}</time>
        </li>
      ))}
    </ol>
  );
}

/**
 * Format a persisted instant.
 *
 * Rendered with a pinned UTC locale and timezone so every viewer — and every
 * render of the same value — agrees. A client-timezone format would make two
 * users disagree about when an order shipped, and a hydration mismatch is
 * worse than a slightly odd format. The machine-readable value stays in the
 * `dateTime` attribute for anything that needs the exact instant.
 */
function formatInstant(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;

  return parsed.toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  });
}