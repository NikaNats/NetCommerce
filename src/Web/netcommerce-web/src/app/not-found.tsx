import Link from 'next/link';
import type { Route } from 'next';

/**
 * Not-found boundary for the whole app.
 *
 * Reached by `notFound()` from the product pages. Deliberately distinct from the
 * product-page outage notice: that one says "our fault, the item still exists",
 * and this one says "there is no such page". Collapsing the two would tell a
 * shopper an item was withdrawn when the service is merely unreachable.
 */
export default function NotFound() {
  return (
    <main id="main" className="stack rail">
      <section className="panel stack" aria-labelledby="nf-heading">
        <h1 id="nf-heading" className="heading-major">
          Page not found
        </h1>
        <p style={{ margin: 0 }}>
          There is no page at this address. If you followed a link from the
          catalog, the product may have been removed.
        </p>
        <div className="row">
          <Link className="btn" href={'/catalog' as Route}>
            Browse the catalog
          </Link>
          <Link className="btn btn--ghost" href={'/' as Route}>
            Go to the storefront
          </Link>
        </div>
      </section>
    </main>
  );
}