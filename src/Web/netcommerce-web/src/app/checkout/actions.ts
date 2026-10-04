'use server';

import { revalidatePath } from 'next/cache';
import { redirect } from 'next/navigation';
import type { Route } from 'next';

import { ApiError } from '@/lib/api/client.server';
import { actionErrorMessage } from '@/lib/api/action-error';
import { clearBasket, getBasket } from '@/lib/api/catalog.server';
import { newIdempotencyKey } from '@/lib/api/headers';
import { requireSession } from '@/lib/auth/guards';
import { canCheckout } from '@/lib/basket/basket';
import { createOrder } from '@/lib/orders/orders.server';
import {
  hasCheckoutErrors,
  isGuid,
  validateCheckoutInput,
  type CheckoutAddressInput,
  type CheckoutErrors,
} from '@/lib/orders/checkout';

export interface PlaceOrderState {
  ok: boolean;
  message: string;
  errors: CheckoutErrors;
}

const INITIAL_ERRORS: CheckoutErrors = {};

function field(formData: FormData, name: string): string {
  return String(formData.get(name) ?? '').trim();
}

function readAddress(formData: FormData, prefix: string): CheckoutAddressInput {
  return {
    recipientName: field(formData, `${prefix}.recipientName`),
    street: field(formData, `${prefix}.street`),
    city: field(formData, `${prefix}.city`),
    state: field(formData, `${prefix}.state`),
    postalCode: field(formData, `${prefix}.postalCode`),
    country: field(formData, `${prefix}.country`),
    phone: field(formData, `${prefix}.phone`),
  };
}

/**
 * POST /api/v1/orders — the only route that turns a basket into an order.
 *
 * Items come from a server-side basket re-read, NEVER from the form: a
 * client-supplied product list would let the caller set their own contents
 * (and, without ExpectedPrice, silently accept a moved price). The form only
 * carries who/where, minted once per page load as the idempotency key so a
 * retry of the same logical order cannot create a second one.
 */
export async function placeOrderAction(
  _prev: PlaceOrderState,
  formData: FormData,
): Promise<PlaceOrderState> {
  await requireSession('/checkout');

  const sameAsBilling = formData.get('sameAsBilling') === 'on';
  const shipping = readAddress(formData, 'shipping');
  const billing = sameAsBilling ? { ...shipping } : readAddress(formData, 'billing');

  const errors = validateCheckoutInput({
    customerName: field(formData, 'customerName'),
    customerEmail: field(formData, 'customerEmail'),
    shipping,
    billing: sameAsBilling ? undefined : billing,
    sameAsBilling,
    couponCode: field(formData, 'couponCode') || undefined,
  });

  if (hasCheckoutErrors(errors)) {
    return { ok: false, message: 'Check the highlighted fields.', errors };
  }

  // The form's key identifies this logical order across retries. A tampered or
  // missing key falls back to a fresh one — which is safe (a new order) rather
  // than safe-looking but wrong (reusing another order's key).
  const rawKey = field(formData, 'idempotencyKey');
  const idempotencyKey = isGuid(rawKey) ? rawKey : newIdempotencyKey();

  let orderId: string;
  try {
    const basket = await getBasket();
    const lines = basket.items.filter((item) => item.quantity > 0);

    if (!canCheckout(basket)) {
      return { ok: false, message: 'Your basket is empty.', errors: INITIAL_ERRORS };
    }

    const created = await createOrder(
      {
        customerEmail: field(formData, 'customerEmail'),
        customerName: field(formData, 'customerName'),
        items: lines.map((item) => ({
          productId: item.productId,
          quantity: item.quantity,
          expectedPrice: item.price,
        })),
        shippingAddress: {
          street: shipping.street,
          city: shipping.city,
          state: shipping.state,
          postalCode: shipping.postalCode,
          country: shipping.country,
          recipientName: shipping.recipientName,
          phoneNumber: shipping.phone,
        },
        billingAddress: {
          street: billing.street,
          city: billing.city,
          state: billing.state,
          postalCode: billing.postalCode,
          country: billing.country,
          recipientName: billing.recipientName,
          phoneNumber: '',
        },
        // No card data is collected here by design: payment is resolved
        // server-side by the fulfillment saga (which falls back to a mock
        // token in development), so the storefront stays out of PCI scope.
        // The command record requires the field, hence the constant.
        paymentMethod: 'card',
        couponCode: field(formData, 'couponCode') || undefined,
      },
      idempotencyKey,
    );
    orderId = created.id;
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 409) {
      // The server's price guard: the catalog moved under the basket. The
      // basket is UNCHANGED — send the user back to review it, not to retry
      // blindly into the same conflict. The revalidate is behavior, so this
      // branch stays in the action; the wording of everything else is owned
      // by actionErrorMessage.
      revalidatePath('/basket');
      return {
        ok: false,
        message:
          'A price changed while you were checking out. Your basket is unchanged — review it and try again.',
        errors: INITIAL_ERRORS,
      };
    }

    return {
      ok: false,
      message: actionErrorMessage(cause, {
        401: 'Your sign-in expired. Sign in again to place the order.',
      }),
      errors: INITIAL_ERRORS,
    };
  }

  // The order exists; the basket is now stale. Clearing is best-effort: if it
  // fails the user still has a valid order, and the basket page will show the
  // leftover lines rather than silently losing them.
  try {
    await clearBasket(newIdempotencyKey());
  } catch (cause) {
    console.warn('[checkout] order placed but basket clear failed', cause);
  }

  revalidatePath('/basket');
  redirect(`/orders/${orderId}` as Route);
}
