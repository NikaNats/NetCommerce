import type { Metadata } from 'next';
import Link from 'next/link';
import type { Route } from 'next';

import { searchProducts } from '@/lib/api/catalog.server';
import { ProductGrid } from '@/components/catalog/product-card';
import { ArrowIcon } from '@/components/icons';
import type {
  PaginationMetadata,
  ProductListItem,
} from '@/lib/catalog/products';

export const metadata: Metadata = {
  title: 'Catalog',
  description: 'Every published product in the NetCommerce catalog.',
};

/**
 * Catalog index.
 *
 * AllowAnonymous on the server, so this renders for signed-out visitors — the
 * catalog is browsable without a session. Only the basket and checkout need one.
 *
 * Search and paging are read from the query string so the view is shareable and
 * survives a refresh. The page stays dynamic because it reads searchParams.
 */
export default async function CatalogPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const params = await searchParams;

  const first = (value: string | string[] | undefined): string | undefined =>
    Array.isArray(value) ? value[0] : value;

  const searchTerm = first(params.q)?.trim() || undefined;
  const pageRaw = Number(first(params.page) ?? '1');
  const page = Number.isFinite(pageRaw) && pageRaw > 0 ? Math.floor(pageRaw) : 1;

  let payload = null;
  let apiError: string | null = null;

  try {
    payload = await searchProducts({ searchTerm, page });
  } catch (cause) {
    apiError = cause instanceof Error ? cause.message : 'Unknown error';
  }

  const pagination = payload?.pagination;
  const pageHref = (target: number) => {
    const query = new URLSearchParams();
    if (searchTerm) query.set('q', searchTerm);
    query.set('page', String(target));
    return `/catalog?${query.toString()}`;
  };

  return (
    <main id="main" className="stack rail">
      <header className="stack" style={{ gap: '0.75rem' }}>
        <h1 className="heading-major">Catalog</h1>
        <p className="lede">
          Published items only. Stock is reserved before payment is taken.
        </p>
      </header>

      {/* GET form: a plain submit keeps this working without JavaScript. */}
      <form className="searchbar" method="get" action="/catalog" role="search">
        <label className="field-label" htmlFor="catalog-q">
          Search
        </label>
        <input
          id="catalog-q"
          className="searchbar__input"
          type="search"
          name="q"
          defaultValue={searchTerm ?? ''}
          placeholder="walnut desk"
          autoComplete="off"
        />
        <button className="btn" type="submit">
          Search
        </button>
      </form>

      {apiError ? (
        <section className="notice" role="status">
          <strong>Catalog unavailable</strong>
          <p style={{ margin: 0 }}>
            <span className="figure">{apiError}</span> — is the API running under
            the Aspire AppHost?
          </p>
        </section>
      ) : null}

      {payload && pagination ? (
        <CatalogResults
          products={payload.items}
          pagination={pagination}
          searchTerm={searchTerm}
          pageHref={pageHref}
        />
      ) : null}
    </main>
  );
}

/**
 * Rendered only when a payload exists, so `pagination` arrives as a required
 * prop. Keeping it in its own component is what makes that guarantee real:
 * inline, `payload?.pagination` left every field "possibly undefined" because
 * the optional chain does not narrow through the enclosing JSX branch.
 */
function CatalogResults({
  products,
  pagination,
  searchTerm,
  pageHref,
}: {
  products: ProductListItem[];
  pagination: PaginationMetadata;
  searchTerm?: string;
  pageHref: (page: number) => string;
}) {
  return (
    <>
      <p className="figure" role="status">
        {pagination.totalCount} item{pagination.totalCount === 1 ? '' : 's'}
        {searchTerm ? ` matching “${searchTerm}”` : ''} · page {pagination.page} of{' '}
        {Math.max(1, pagination.totalPages)}
      </p>

      <ProductGrid products={products} />

      {pagination.totalPages > 1 ? (
        <nav className="pager" aria-label="Catalog pages">
          {pagination.hasPreviousPage ? (
            <Link
              className="btn btn--ghost"
              href={pageHref(pagination.page - 1) as Route}
            >
              Previous
            </Link>
          ) : (
            <span className="btn btn--ghost is-disabled" aria-disabled="true">
              Previous
            </span>
          )}

          {pagination.hasNextPage ? (
            <Link
              className="btn btn--ghost"
              href={pageHref(pagination.page + 1) as Route}
            >
              Next <ArrowIcon />
            </Link>
          ) : (
            <span className="btn btn--ghost is-disabled" aria-disabled="true">
              Next
            </span>
          )}
        </nav>
      ) : null}
    </>
  );
}