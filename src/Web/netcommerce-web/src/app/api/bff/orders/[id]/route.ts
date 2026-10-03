import { ApiError, apiFetch } from '@/lib/api/client.server';
import { getSession } from '@/lib/auth/server-session';

/**
 * BFF proxy for order reads.
 *
 * ## Why this exists rather than the browser calling the API
 *
 * `useOrderSaga.reconcile()` needs the authoritative persisted order state, but it
 * runs in a Client Component. A direct browser fetch would need a bearer token in
 * the browser, which this architecture refuses to have. Routing through a route
 * handler keeps the token in the server-side session: the browser addresses THIS
 * origin, the server attaches the credential.
 *
 * It also means the browser never learns the API's base URL.
 *
 * ## Why the session check is only a fast path
 *
 * The API enforces ownership — the JWT subject must be the ordering customer — and
 * this handler deliberately does not re-implement that. It only refuses early when
 * there is provably no session, avoiding a pointless round trip. Anything past that
 * is the API's decision, and its status is passed through rather than
 * reinterpreted, so a legitimate 403 can never surface as a 404.
 */

/**
 * The order projection the API returns.
 *
 * `status` is the PERSISTED OrderStatus enum value, which the consumer maps with
 * mapOrderStatus — NOT the saga's free-form push strings, which are a different
 * vocabulary. Conflating them is a bug this shape is meant to prevent.
 */
export interface OrderSummaryResponse {
  id: string;
  orderNumber: string;
  status: number;
  createdAt: string;
  paidAt?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
}

export async function GET(
  _request: Request,
  context: { params: Promise<{ id: string }> },
): Promise<Response> {
  const { id } = await context.params;

  // Awaited: getSession is async and reads cookies. The result is only used as a
  // truthiness check — apiFetch resolves the session again for the bearer token,
  // and the API is the authority on ownership either way.
  if (!(await getSession())) {
    return Response.json({ error: 'Sign in to view this order.' }, { status: 401 });
  }

  try {
    const order = await apiFetch<OrderSummaryResponse>(
      `/api/v1/orders/${encodeURIComponent(id)}`,
      {
        // Reconciliation runs on load and on every reconnect. A cached response
        // would defeat the entire purpose of reconciling against authoritative
        // state, so this must not become a stale read.
        method: 'GET',
      },
    );

    return Response.json(order);
  } catch (cause) {
    // Pass the API's status through. Collapsing it into a 500 would make a
    // legitimate 403 (someone else's order) look like an outage, and would
    // discard the correlation id an operator needs.
    if (cause instanceof ApiError) {
      return Response.json(
        { error: cause.message, correlationId: cause.correlationId },
        { status: cause.status },
      );
    }

    console.error('[bff/orders] unexpected failure', cause);
    return Response.json({ error: 'Could not reach the order service.' }, { status: 502 });
  }
}