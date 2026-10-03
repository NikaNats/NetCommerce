import type { Metadata } from 'next';
import { notFound } from 'next/navigation';

import { getProductById } from '@/lib/api/catalog.server';
import { isPublished, primaryImage } from '@/lib/catalog/products';
import { formatMoney } from '@/lib/format/money';
import { AddToCartForm } from '@/components/catalog/add-to-cart-form';

interface PageProps {
  params: Promise<{ id: string }>;
}

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { id } = await params;

  if (!GUID_PATTERN.test(id)) return { title: 'Not found' };

  try {
    const product = await getProductById(id);
    if (!product) return { title: 'Not found' };

    return {
      title: product.seoTitle || product.name,
      description: product.seoDescription || product.description || undefined,
    };
  } catch {
    // Same contract as the slug route: an API fault is not "not found".
    return { title: 'Temporarily unavailable', robots: { index: false } };
  }
}

/**
 * Product detail addressed by GUID: /products/id/{id}
 *
 * This exists because productHref falls back to the id route when a product has
 * no slug. Without this page that fallback would link to a 404 — the card would
 * look fine in a grid and break on click.
 *
 * A malformed id is a 404 here rather than a 500: the route constrains the
 * segment to a guid, so anything else never reaches the API.
 */
export default async function ProductByIdPage({ params }: PageProps) {
  const { id } = await params;

  if (!GUID_PATTERN.test(id)) notFound();

  let product = null;
  let fault: string | null = null;

  try {
    // getProductById already maps a genuine 404 to null, so a null product with
    // no fault means "no such product" — that is notFound(), not the outage
    // notice below. Only non-404 faults reach the catch.
    product = await getProductById(id);
  } catch (cause) {
    fault =
      cause instanceof Error
        ? cause.message
        : 'The catalog service could not be reached.';
  }

  // A missing product is a normal 404, handled exactly like the slug route: a
  // null with no fault is notFound(). Rendering the outage notice here would
  // claim the service is down when the truth is the item does not exist — the
  // mirror image of the slug route's bug.
  if (!product && !fault) notFound();

  if (fault || !product) {
    return (
      <main id="main" className="stack rail">
        <nav className="crumbs field-label" aria-label="Breadcrumb">
          <a href="/catalog">Catalog</a>
        </nav>
        <section className="notice" role="alert">
          <strong>Catalog temporarily unavailable</strong>
          <p style={{ margin: 0 }}>
            We could not reach the catalog service, so this product cannot be
            shown right now. This is a fault on our side — the item has not been
            withdrawn.
          </p>
          <p className="figure" style={{ margin: '0.5rem 0 0' }}>
            {fault}
          </p>
          <div style={{ marginTop: '1rem' }}>
            <a className="btn" href="/catalog">
              Back to catalog
            </a>
          </div>
        </section>
      </main>
    );
  }

  const hero = primaryImage(product.images);
  const purchasable = isPublished(product.status);

  return (
    <main id="main" className="stack rail">
      <nav className="crumbs field-label" aria-label="Breadcrumb">
        <a href="/catalog">Catalog</a>
        <span aria-hidden="true"> / </span>
        <span>{product.name}</span>
      </nav>

      <div className="detail">
        <div className="detail__media">
          {hero ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={hero.url} alt={product.name} className="detail__image" />
          ) : (
            <div className="detail__placeholder" aria-hidden="true">
              <span className="figure">{product.sku}</span>
            </div>
          )}
        </div>

        <div className="detail__body stack">
          <div className="stack" style={{ gap: '0.5rem' }}>
            {product.categoryName ? (
              <p className="field-label">{product.categoryName}</p>
            ) : null}
            <h1 className="heading-major">{product.name}</h1>
            <p className="figure detail__sku">{product.sku}</p>
          </div>

          <p className="detail__price figure">
            {formatMoney(product.price, product.currency)}
          </p>

          {product.description ? (
            <p className="prose">{product.description}</p>
          ) : null}

          {purchasable ? (
            <AddToCartForm
                          productId={product.id}
                          productName={product.name}
                        />
          ) : (
            <p className="notice" role="status">
              This item is <strong>{product.status.toLowerCase()}</strong> and is
              not available to buy yet.
            </p>
          )}
        </div>
      </div>
    </main>
  );
}