'use client';

import { useActionState } from 'react';

import {
  cancelOrderAction,
  type CancelOrderState,
} from '@/app/orders/[id]/actions';

const INITIAL: CancelOrderState = { ok: false, message: '' };

/**
 * Cancellation for a cancellable order.
 *
 * Rendered for every non-terminal persisted status — the server re-checks
 * ownership and state, so this gate is UX, not security. The copy depends on
 * the grace period (see isInGracePeriod): inside it cancellation is instant
 * and free because payment was never taken; past it the refund path runs, and
 * promising "free" would be false.
 *
 * A reason is optional; the API defaults it when blank.
 */
export function CancelOrderForm({
  orderId,
  inGracePeriod,
}: {
  orderId: string;
  inGracePeriod: boolean;
}) {
  const [state, action, pending] = useActionState(cancelOrderAction, INITIAL);

  return (
    <section className="panel stack" aria-label="Cancel this order">
      <h2 className="heading-minor" style={{ margin: 0 }}>
        Changed your mind?
      </h2>
      <p style={{ margin: 0 }}>
        {inGracePeriod
          ? 'Cancellation is instant and free while the order is still being prepared — no payment has been taken.'
          : 'This order is already being processed. Cancelling now releases the stock and refunds any payment taken.'}
      </p>
      <form action={action} className="stack">
        <input type="hidden" name="orderId" value={orderId} />
        <div className="field">
          <label className="field-label" htmlFor={`cancel-reason-${orderId}`}>
            Reason (optional)
          </label>
          <input
            id={`cancel-reason-${orderId}`}
            name="reason"
            type="text"
            maxLength={200}
            placeholder="Ordered by mistake"
            disabled={pending}
          />
        </div>
        {state.message ? (
          <p className="notice" role="status" style={{ margin: 0 }}>
            {state.message}
          </p>
        ) : null}
        <div>
          <button className="btn btn--ghost" type="submit" disabled={pending}>
            {pending ? 'Cancelling…' : 'Cancel order'}
          </button>
        </div>
      </form>
    </section>
  );
}
