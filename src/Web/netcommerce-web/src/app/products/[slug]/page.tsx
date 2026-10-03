import type { Metadata } from 'next';
import { notFound } from 'next/navigation';

import { ApiError } from '@/lib/api/client.server';
import { getProductBySlug } from '@/lib/api/catalog.server';
import { isPublished, primaryImage } from '@/lib/catalog/products';
import { formatMoney } from '@/lib/format/money';
import { AddToCartForm } from '@/components/catalog/add-to-cart-form';

interface PageProps {
  params: Promise<{ slug: string }>;
}

/**
 * An unreachable API must not look like a missing product.
 *
 * Only a genuine 404 becomes notFound(). Everything else — the API down, a 500,
 * a timeout — renders an explicit fault. The distinction matters: a 404 tells a
 * shopper the item does not exist, which is a false statement when the truth is
 * that the service is unreachable. Re-throwing here produced a 500 page, which at
 * least is honest but unhelpful; the notice below names the actual cause.
 */
async function loadProduct(slug: string) {
  try {
    return await getProductBySlug(slug);
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 404) return null;
    throw cause;
  }
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { slug } = await params;

  try {
    const product = await getProductBySlug(slug);
    if (!product) return { title: 'Not found' };

    return {
      // Prefer the server's own SEO fields; fall back to the name so a product
      // is never published with an empty <title>.
      title: product.seoTitle || product.name,
      description: product.seoDescription || product.description || undefined,
    };
  } catch {
    // An API fault is NOT "not found". Returning the Not found title here put a
    // "Not found" in the browser tab while the body rendered a service-outage
    // notice — a contradiction a user sees first. Title it as unavailable.
    return { title: 'Temporarily unavailable', robots: { index: false } };
  }
}

/**
 * Product detail, addressed by slug: /products/{slug}
 *
 * AllowAnonymous on the server — a product page is browsable signed out. Only the
 * add-to-basket action requires a session.
 */
export default async function ProductPage({ params }: PageProps) {
  const { slug } = await params;

  let product = null;
  let fault: string | null = null;

  try {
    product = await loadProduct(slug);
  } catch (cause) {
    fault =
      cause instanceof Error
        ? cause.message
        : 'The catalog service could not be reached.';
  }

  if (fault) {
    return (
      <main id="main" className="stack rail">
        <nav className="crumbs field-label" aria-label="Breadcrumb">
          <a href="/catalog">Catalog</a>
        </nav>
        <section className="notice" role="alert">
          <strong>Catalog temporarily unavailable</strong>
          <p style={{ margin: 0 }}>
            We could not reach the catalog service, so this product cannot be shown
            right now. This is a fault on our side — the item has not been
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

  if (!product) notFound();

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
            <img
              src={hero.url}
              alt={product.name}
              className="detail__image"
              // The LCP element: never lazy-load it.
              loading="eager"
              fetchPriority="high"
            />
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

          {product.attributes.length > 0 ? (
            <table className="specs">
              <caption className="sr-only">Specifications for {product.name}</caption>
              <tbody>
                {product.attributes.map((attribute) => (
                  <tr key={attribute.key}>
                    <th scope="row" className="field-label">
                      {attribute.displayName || attribute.key}
                    </th>
                    <td className="figure">{attribute.value}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}

          {purchasable ? (
            <AddToCartForm
              productId={product.id}
              productName={product.name}
            />
          ) : (
            // Not-published is a normal state, not an error. Say so plainly
            // instead of rendering a buy button that would fail.
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