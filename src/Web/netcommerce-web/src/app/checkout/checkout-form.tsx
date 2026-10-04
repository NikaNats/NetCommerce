'use client';

import { useActionState, useState } from 'react';

import { placeOrderAction, type PlaceOrderState } from '@/app/checkout/actions';
import type { CheckoutErrors } from '@/lib/orders/checkout';
import { formatMoney } from '@/lib/format/money';

const INITIAL: PlaceOrderState = { ok: false, message: '', errors: {} };

export interface CheckoutDefaults {
  customerName: string;
  customerEmail: string;
  idempotencyKey: string;
}

export interface CheckoutLine {
  productId: string;
  productName: string;
  quantity: number;
  price: number;
}

function FieldError({ message }: { message?: string }) {
  if (!message) return null;
  return (
    <p className="field-error" role="alert" style={{ margin: 0 }}>
      {message}
    </p>
  );
}

/**
 * Checkout form, as a Client Component.
 *
 * One submission, one idempotency key: the key arrives as a prop minted by the
 * page (one per page load) and rides in a hidden field, so retrying the same
 * logical order reuses it instead of creating a second order. Items and totals
 * are DISPLAY ONLY — the action re-reads the basket server-side.
 */
export function CheckoutForm({
  lines,
  totalPrice,
  defaults,
}: {
  lines: CheckoutLine[];
  totalPrice: number;
  defaults: CheckoutDefaults;
}) {
  const [state, action, pending] = useActionState(placeOrderAction, INITIAL);
  const [sameAsBilling, setSameAsBilling] = useState(true);

  const errors: CheckoutErrors = state.errors;

  return (
    <form action={action} className="stack">
      <input type="hidden" name="idempotencyKey" value={defaults.idempotencyKey} />

      <fieldset className="panel stack" disabled={pending}>
        <legend className="heading-minor">Contact</legend>
        <div className="field">
          <label className="field-label" htmlFor="checkout-name">
            Full name
          </label>
          <input
            id="checkout-name"
            name="customerName"
            type="text"
            defaultValue={defaults.customerName}
            required
            autoComplete="name"
          />
          <FieldError message={errors.customerName} />
        </div>
        <div className="field">
          <label className="field-label" htmlFor="checkout-email">
            Email
          </label>
          <input
            id="checkout-email"
            name="customerEmail"
            type="email"
            defaultValue={defaults.customerEmail}
            required
            autoComplete="email"
          />
          <FieldError message={errors.customerEmail} />
        </div>
      </fieldset>

      <fieldset className="panel stack" disabled={pending}>
        <legend className="heading-minor">Shipping address</legend>
        <AddressFields prefix="shipping" errors={errors} />
      </fieldset>

      <fieldset className="panel stack" disabled={pending}>
        <legend className="heading-minor">Billing address</legend>
        <label className="row" style={{ gap: '0.5rem' }}>
          <input
            type="checkbox"
            name="sameAsBilling"
            checked={sameAsBilling}
            onChange={(event) => setSameAsBilling(event.target.checked)}
          />
          <span>Same as shipping address</span>
        </label>
        {sameAsBilling ? null : <AddressFields prefix="billing" errors={errors} />}
      </fieldset>

      <fieldset className="panel stack" disabled={pending}>
        <legend className="heading-minor">Discount</legend>
        <div className="field">
          <label className="field-label" htmlFor="checkout-coupon">
            Coupon code (optional)
          </label>
          <input
            id="checkout-coupon"
            name="couponCode"
            type="text"
            autoComplete="off"
            placeholder="SUMMER10"
          />
          <FieldError message={errors.couponCode} />
        </div>
      </fieldset>

      <section className="basket__summary panel stack" aria-label="Order summary">
        <div className="row" style={{ justifyContent: 'space-between' }}>
          <h2 className="heading-minor" style={{ margin: 0 }}>
            Total due
          </h2>
          <p className="basket__total figure" style={{ margin: 0 }}>
            {formatMoney(totalPrice, null)}
          </p>
        </div>
        <p className="field-label" style={{ margin: 0 }}>
          {lines.length} line{lines.length === 1 ? '' : 's'} · final pricing is
          confirmed by the server at order time
        </p>
        {state.message ? (
          <p className="notice" role="status" style={{ margin: 0 }}>
            {state.message}
          </p>
        ) : null}
        <button className="btn" type="submit" disabled={pending}>
          {pending ? 'Placing order…' : 'Place order'}
        </button>
      </section>
    </form>
  );
}

function AddressFields({
  prefix,
  errors,
}: {
  prefix: 'shipping' | 'billing';
  errors: CheckoutErrors;
}) {
  const at = (name: 'recipientName' | 'street' | 'city' | 'state' | 'postalCode' | 'country' | 'phone') =>
    errors[`${prefix}.${name}`];

  return (
    <>
      <div className="field">
        <label className="field-label" htmlFor={`${prefix}-recipient`}>
          Recipient
        </label>
        <input
          id={`${prefix}-recipient`}
          name={`${prefix}.recipientName`}
          type="text"
          required
          autoComplete={prefix === 'shipping' ? 'shipping name' : 'billing name'}
        />
        <FieldError message={at('recipientName')} />
      </div>
      <div className="field">
        <label className="field-label" htmlFor={`${prefix}-street`}>
          Street
        </label>
        <input
          id={`${prefix}-street`}
          name={`${prefix}.street`}
          type="text"
          required
          autoComplete="street-address"
        />
        <FieldError message={at('street')} />
      </div>
      <div className="row" style={{ gap: '1rem' }}>
        <div className="field">
          <label className="field-label" htmlFor={`${prefix}-city`}>
            City
          </label>
          <input id={`${prefix}-city`} name={`${prefix}.city`} type="text" required />
          <FieldError message={at('city')} />
        </div>
        <div className="field">
          <label className="field-label" htmlFor={`${prefix}-postal`}>
            Postal code
          </label>
          <input id={`${prefix}-postal`} name={`${prefix}.postalCode`} type="text" />
          <FieldError message={at('postalCode')} />
        </div>
      </div>
      <div className="row" style={{ gap: '1rem' }}>
        <div className="field">
          <label className="field-label" htmlFor={`${prefix}-state`}>
            State / region
          </label>
          <input id={`${prefix}-state`} name={`${prefix}.state`} type="text" />
          <FieldError message={at('state')} />
        </div>
        <div className="field">
          <label className="field-label" htmlFor={`${prefix}-country`}>
            Country
          </label>
          <input
            id={`${prefix}-country`}
            name={`${prefix}.country`}
            type="text"
            required
            autoComplete="country-name"
          />
          <FieldError message={at('country')} />
        </div>
      </div>
      {prefix === 'shipping' ? (
        <div className="field">
          <label className="field-label" htmlFor="shipping-phone">
            Phone
          </label>
          <input id="shipping-phone" name="shipping.phone" type="tel" autoComplete="tel" />
          <FieldError message={at('phone')} />
        </div>
      ) : null}
    </>
  );
}
