import type { Metadata } from 'next';
import Link from 'next/link';
import type { Route } from 'next';

import { ApiError, fetchSessionInfo } from '@/lib/api/client.server';
import { getBasket } from '@/lib/api/catalog.server';
import { newIdempotencyKey } from '@/lib/api/headers';
import { requireSession } from '@/lib/auth/guards';
import { canCheckout } from '@/lib/basket/basket';
import { CheckoutForm } from '@/app/checkout/checkout-form';
import { ArrowIcon } from '@/components/icons';

export const metadata: Metadata = {
  title: 'Checkout',
  description: 'Confirm your details and place your order.',
};

export const dynamic = 'force-dynamic';

/**
 * Checkout.
 *
 * Requires a session: POST /api/v1/orders is CustomerOnly, and an anonymous
 * visitor must be sent to login instead of filling an order form that cannot
 * submit. The basket is re-read here for display AND again inside the action
 * for the actual order — the action's copy is authoritative, this one is paint.
 */
export default async function CheckoutPage() {
  await requireSession('/checkout');

  let basket = null;
  let apiError: string | null = null;
  let forbidden = false;

  try {
    basket = await getBasket();
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) {
      forbidden = true;
    } else {
      apiError = cause instanceof Error ? cause.message : 'Unknown error';
    }
  }

  // Contact prefill from the backend's own introspection — never a locally
  // decoded token. Missing fields degrade to empty inputs, not a failure.
  const sessionInfo = await fetchSessionInfo().catch(() => null);

  // One logical order, one key: minted per page load so a retry of the same
  // submission reuses it (no duplicate order) while a fresh visit starts clean.
  const idempotencyKey = newIdempotencyKey();

  const lines = basket?.items.filter((item) => item.quantity > 0) ?? [];

  return (
    <main id="main" className="stack rail">
      <header className="stack" style={{ gap: '0.5rem' }}>
        <h1 className="heading-major">Checkout</h1>
      </header>

      {forbidden ? (
        <section className="notice" role="status">
          <strong>Session expired</strong>
          <p style={{ margin: '0 0 1rem' }}>
            Your sign-in is no longer valid. Sign in again to check out.
          </p>
          <Link className="btn" href={'/login?returnTo=%2Fcheckout' as Route}>
            Sign in <ArrowIcon />
          </Link>
        </section>
      ) : null}

      {apiError ? (
        <section className="notice" role="status">
          <strong>Checkout unavailable</strong>
          <p style={{ margin: 0 }}>
            <span className="figure">{apiError}</span> — is the API running under
            the Aspire AppHost?
          </p>
        </section>
      ) : null}

      {basket && !canCheckout(basket) ? (
        <section className="panel stack">
          <h2 className="heading-minor">Nothing to check out</h2>
          <p style={{ margin: 0 }}>Your basket is empty.</p>
          <div>
            <Link className="btn" href={'/catalog' as Route}>
              Browse the catalog <ArrowIcon />
            </Link>
          </div>
        </section>
      ) : null}

      {basket && canCheckout(basket) ? (
        <CheckoutForm
          lines={lines.map((item) => ({
            productId: item.productId,
            productName: item.productName,
            quantity: item.quantity,
            price: item.price,
          }))}
          totalPrice={basket.totalPrice}
          defaults={{
            customerName: sessionInfo?.username ?? '',
            customerEmail: sessionInfo?.email ?? '',
            idempotencyKey,
          }}
        />
      ) : null}
    </main>
  );
}
