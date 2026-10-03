import type { Metadata } from 'next';

import OrderTracker from '@/components/orders/order-tracker';
import { ApiError, apiFetch } from '@/lib/api/client.server';
import { requireSession } from '@/lib/auth/guards';
import { mapOrderStatus } from '@/lib/orders/order-status';

export const metadata: Metadata = {
  title: 'Your order',
  description: 'Live status and progress for your order.',
};

/**
 * Order detail and live progress.
 *
 * ## Why the page is split in two
 *
 * The SERVER component renders the authoritative state it can already see — the
 * order number, the persisted status, the timeline — and hands it to a small client
 * component that owns the socket. That split is the whole point:
 *
 *   - the first paint is correct WITHOUT waiting for a websocket, because it comes
 *     from the persisted order rather than the stream;
 *   - the token never reaches the browser, because only the client component
 *     talks to the hub, and it authenticates with the session cookie;
 *   - the order payload is not shipped as a client bundle beyond what the tracker
 *     actually needs.
 *
 * ## Why reconciliation still exists
 *
 * The socket is an accelerator, not the source of truth. It can miss transitions
 * during a reconnect and may never deliver at all. The tracker therefore re-reads
 * this same order over the BFF route on connect, on reconnect, and on an interval.
 */
export default async function OrderPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  // Sign-in is required BEFORE the fetch, not after: this route must never render
  // another customer's order, and requireSession redirects rather than throwing, so
  // it is the correct tool here where the user is navigating to a page.
  await requireSession(`/orders/${id}`);

  try {
    const order = await apiFetch<{
      id: string;
      orderNumber: string;
      status: number;
      createdAt: string;
      cancellationReason?: string | null;
    }>(`/api/v1/orders/${encodeURIComponent(id)}`);

    // mapOrderStatus, NOT mapRealtimeStatus: this is the PERSISTED enum, and the
    // realtime mapper only knows the saga's free-form push strings. Passing one to
    // the other yields "Unknown(...)" and a false "status unavailable" banner.
    const statusName = mapOrderStatus(order.status);

    return (
      <main id="main" className="shell">
        <p className="field-label">Order {order.orderNumber}</p>
        <h1>Your order</h1>

        <OrderTracker
          orderId={order.id}
          initialStatus={statusName}
          orderNumber={order.orderNumber}
          placedAt={order.createdAt}
          cancellationReason={order.cancellationReason ?? null}
        />
      </main>
    );
  } catch (cause) {
    // Distinguish "not yours / not there" from "the API is down". Showing
    // "Something went wrong" for a 403 would send the user chasing a support ticket
    // for what is actually an ownership boundary.
    if (cause instanceof ApiError && (cause.status === 403 || cause.status === 404)) {
      return (
        <main id="main" className="shell">
          <h1>Order not available</h1>
          <p className="notice" role="status">
            This order does not exist, or it belongs to another account.
          </p>
        </main>
      );
    }

    throw cause;
  }
}