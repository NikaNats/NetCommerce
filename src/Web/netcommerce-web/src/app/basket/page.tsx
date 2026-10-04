import type { Metadata } from 'next';
import Link from 'next/link';
import type { Route } from 'next';

import { getBasket } from '@/lib/api/catalog.server';
import { ApiError } from '@/lib/api/client.server';
import { canCheckout, distinctItemCount } from '@/lib/basket/basket';
import { requireSession } from '@/lib/auth/guards';
import { formatMoney } from '@/lib/format/money';
import { BasketLine } from '@/components/basket/basket-line';
import { ArrowIcon } from '@/components/icons';

export const metadata: Metadata = {
  title: 'Basket',
};

export const dynamic = 'force-dynamic';

/**
 * Basket.
 *
 * Requires a session: the whole /api/v1/basket group is `.RequireAuthorization()`
 * and rate limited per user. requireSession runs before any fetch so an
 * anonymous visitor is sent to login instead of seeing an empty-basket screen
 * that implies "you have nothing" rather than "you are not signed in".
 */
export default async function BasketPage() {
  await requireSession('/basket');

  let basket = null;
  let apiError: string | null = null;
  let forbidden = false;

  try {
    basket = await getBasket();
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) {
      // The session existed but the API rejected the token (expired, or the SSO
      // session ended). Sign out cleanly rather than showing a broken basket.
      forbidden = true;
    } else {
      apiError = cause instanceof Error ? cause.message : 'Unknown error';
    }
  }

  const lines = basket?.items.filter((item) => item.quantity > 0) ?? [];
  const count = basket ? distinctItemCount(basket) : 0;

  return (
    <main id="main" className="stack rail">
      <header className="stack" style={{ gap: '0.5rem' }}>
        <h1 className="heading-major">Basket</h1>
        {basket ? (
          <p className="figure">
            {count} line{count === 1 ? '' : 's'}
          </p>
        ) : null}
      </header>

      {forbidden ? (
        <section className="notice" role="status">
          <strong>Session expired</strong>
          <p style={{ margin: '0 0 1rem' }}>
            Your sign-in is no longer valid. Sign in again to see your basket.
          </p>
          <Link className="btn" href={'/login?returnTo=%2Fbasket' as Route}>
            Sign in <ArrowIcon />
          </Link>
        </section>
      ) : null}

      {apiError ? (
        <section className="notice" role="status">
          <strong>Basket unavailable</strong>
          <p style={{ margin: 0 }}>
            <span className="figure">{apiError}</span> — is the API running under
            the Aspire AppHost?
          </p>
        </section>
      ) : null}

      {basket && lines.length === 0 ? (
        <section className="panel stack">
          <h2 className="heading-minor">Nothing here yet</h2>
          <p style={{ margin: 0 }}>
            Items you add are held server-side against your account, not in this
            browser.
          </p>
          <div>
            <Link className="btn" href={'/catalog' as Route}>
              Browse the catalog <ArrowIcon />
            </Link>
          </div>
        </section>
      ) : null}

      {basket && lines.length > 0 ? (
        <>
          <table className="basket">
            <caption className="sr-only">Items in your basket</caption>
            <thead>
              <tr>
                <th scope="col" className="field-label">
                  Item
                </th>
                <th scope="col" className="field-label">
                  Unit
                </th>
                <th scope="col" className="field-label">
                  Quantity
                </th>
                <th scope="col" className="field-label">
                  Line total
                </th>
                <th scope="col" className="field-label">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {lines.map((item) => (
                <BasketLine key={item.productId} item={item} />
              ))}
            </tbody>
          </table>

          <section className="basket__summary panel stack">
            {/* The server's own total, not a client-side sum. A basket total that
                disagrees with checkout is a financial-integrity bug. */}
            <div className="row" style={{ justifyContent: 'space-between' }}>
              <h2 className="heading-minor" style={{ margin: 0 }}>
                Total
              </h2>
              <p className="basket__total figure" style={{ margin: 0 }}>
                {formatMoney(basket.totalPrice, null)}
              </p>
            </div>

            <p className="field-label" style={{ margin: 0 }}>
              Currency is resolved per item at checkout
            </p>

            <div className="row">
              <Link className="btn btn--ghost" href={'/catalog' as Route}>
                Continue shopping
              </Link>
              {canCheckout(basket) ? (
                <Link className="btn" href={'/checkout' as Route}>
                  Checkout <ArrowIcon />
                </Link>
              ) : null}
            </div>
          </section>
        </>
      ) : null}
    </main>
  );
}