'use client';

import { useActionState } from 'react';

import {
  removeItemAction,
  setQuantityAction,
  type BasketActionState,
} from '@/app/basket/actions';
import type { BasketItem } from '@/lib/basket/basket';
import { formatMoney } from '@/lib/format/money';

const INITIAL: BasketActionState = { ok: false, message: '' };

/**
 * One basket line, as a Client Component.
 *
 * Interactive per row rather than for the whole table so the quantity input and
 * remove button can each own their own pending state — a table-wide form would
 * disable every row while one save is in flight.
 *
 * Each row is its own <form> because Server Actions bind one submission each.
 */
export function BasketLine({ item }: { item: BasketItem }) {
  const [quantityState, quantityAction, quantityPending] = useActionState(
    setQuantityAction,
    INITIAL,
  );
  const [removeState, removeAction, removePending] = useActionState(
    removeItemAction,
    INITIAL,
  );

  const busy = quantityPending || removePending;

  // Announce either action's outcome: a save that silently fails is otherwise
  // indistinguishable from one that worked.
  const outcome =
    quantityState.message || removeState.message
      ? `${item.productName}: ${quantityState.message || removeState.message}`
      : '';

  return (
    <tr className="basket__row">
      <th scope="row" className="basket__name">
        {item.productName}
        {item.sku ? <span className="figure basket__sku">{item.sku}</span> : null}
      </th>

      <td className="figure basket__unit">
        {formatMoney(item.price, null)}
      </td>

      <td className="basket__qty">
        {/* eslint-disable-next-line @next/next/no-sync-scripts */}
        <form action={quantityAction}>
          <input type="hidden" name="productId" value={item.productId} />
          <label className="sr-only" htmlFor={`qty-${item.productId}`}>
            Quantity for {item.productName}
          </label>
          <input
            id={`qty-${item.productId}`}
            name="quantity"
            type="number"
            min={1}
            step={1}
            defaultValue={item.quantity}
            inputMode="numeric"
            disabled={busy}
          />
          <button className="btn btn--ghost" type="submit" disabled={quantityPending}>
            {quantityPending ? 'Saving…' : 'Update'}
          </button>
        </form>
      </td>

      {/* Display-only: the server carries no per-line total, so price × quantity
          is rendered here for the row. The basket TOTAL stays authoritative
          from the server (basket.totalPrice) — this product is never fed back
          into a request or summed into a checkout figure. */}
      <td className="figure basket__line-total">
        {formatMoney(item.price * item.quantity, null)}
      </td>

      <td className="basket__remove">
        <form action={removeAction}>
          <input type="hidden" name="productId" value={item.productId} />
          <button className="btn btn--ghost" type="submit" disabled={removePending}>
            {removePending ? 'Removing…' : 'Remove'}
          </button>
        </form>
      </td>

      {/* Live region: announces the outcome of either action on this row. */}
      <td className="sr-only" role="status" aria-live="polite">
        {outcome}
      </td>
    </tr>
  );
}