import Link from 'next/link';
import type { Route } from 'next';

import {
  isPublished,
  productHref,
  type ProductListItem,
} from '@/lib/catalog/products';
import { formatMoney } from '@/lib/format/money';

/**
 * Product card for the list projection.
 *
 * Deliberately renders only fields the LIST DTO actually carries — id, name, sku,
 * price, currency, primaryImageUrl, status, slug. Reaching for a description or
 * category here would render blanks, because ProductListItemDto has none. The
 * detail page is where those exist.
 */

const imageTone = (seed: string) => {
  // Deterministic placeholder tint derived from the id: stable across renders,
  // so a card does not change colour between the server and client pass.
  let hash = 0;
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) % 360;
  }
  return hash;
};

export function ProductCard({ product }: { product: ProductListItem }) {
  const href = productHref(product);
  const publishable = isPublished(product.status);

  const body = (
    <>
      <div
        className="card__plate"
        style={{ '--plate-hue': imageTone(product.id) } as React.CSSProperties}
        aria-hidden="true"
      >
        {product.primaryImageUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={product.primaryImageUrl}
            alt=""
            className="card__image"
            loading="lazy"
          />
        ) : (
          <span className="card__placeholder figure">
            {product.sku.slice(0, 8)}
          </span>
        )}
      </div>

      <div className="card__body">
        <h3 className="card__title">{product.name}</h3>
        <p className="figure card__sku">{product.sku}</p>
        <p className="card__price figure">{formatMoney(product.price, product.currency)}</p>
      </div>

      {!publishable ? (
        <p className="card__flag field-label">{product.status}</p>
      ) : null}
    </>
  );

  // A row with neither slug nor id cannot be linked. Rendering a bare <a> with an
  // empty href would produce a control that looks actionable and navigates nowhere.
  if (!href) {
    return (
      <article className="card" aria-disabled="true">
        {body}
      </article>
    );
  }

  return (
    <article className="card">
      <Link className="card__link" href={href as Route}>
        {body}
      </Link>
    </article>
  );
}

export function ProductGrid({ products }: { products: readonly ProductListItem[] }) {
  if (products.length === 0) {
    return (
      <p className="notice" role="status">
        Nothing matches that search yet.
      </p>
    );
  }

  return (
    <ul className="grid-cards">
      {products.map((product) => (
        <li key={product.id}>
          <ProductCard product={product} />
        </li>
      ))}
    </ul>
  );
}