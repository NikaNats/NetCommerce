'use client';

import { useActionState } from 'react';

import { addToCartAction, type AddToCartState } from '@/app/products/actions';

const INITIAL: AddToCartState = { ok: false, message: '' };

/**
 * The only client-side interactivity on the product page.
 *
 * A Client Component because `useActionState` needs the pending/error state that
 * a Server Component cannot hold. Everything else on the page stays on the
 * server, so the product payload is never shipped as a client bundle.
 *
 * The form posts through the Server Action, so no token reaches the browser.
 */
export function AddToCartForm({
  productId,
  productName,
  sku,
  unitPrice,
  imageUrl,
}: {
  productId: string;
  productName: string;
  sku: string;
  unitPrice: number;
  imageUrl: string;
}) {
  const [state, formAction, pending] = useActionState(addToCartAction, INITIAL);

  return (
    <form action={formAction} className="addtocart">
      <input type="hidden" name="productId" value={productId} />
      <input type="hidden" name="productName" value={productName} />
      <input type="hidden" name="sku" value={sku} />
      <input type="hidden" name="unitPrice" value={String(unitPrice)} />
      <input type="hidden" name="imageUrl" value={imageUrl} />

      <div className="addtocart__row">
        <label className="field-label" htmlFor="quantity">
          Quantity
        </label>
        <input
          id="quantity"
          className="addtocart__qty"
          type="number"
          name="quantity"
          defaultValue={1}
          min={1}
          step={1}
          inputMode="numeric"
          // Disabled while pending so a double-click cannot submit twice and
          // double the line quantity.
          disabled={pending}
        />
      </div>

      <button className="btn" type="submit" disabled={pending}>
        {pending ? 'Adding…' : 'Add to basket'}
      </button>

      {/* role=status so the outcome is announced, not just shown. */}
      <p
        className={state.ok ? 'addtocart__ok' : 'addtocart__error'}
        role="status"
        aria-live="polite"
      >
        {state.message}
      </p>
    </form>
  );
}