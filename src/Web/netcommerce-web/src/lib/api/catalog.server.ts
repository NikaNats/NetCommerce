import { ApiError, apiFetch } from '@/lib/api/client.server';
import {
  DEFAULT_PAGE_SIZE,
  type PaginatedResponse,
  type Product,
  type ProductListItem,
} from '@/lib/catalog/products';
import type { Category } from '@/lib/catalog/categories';
import type { AddBasketItemRequest, Basket } from '@/lib/basket/basket';
import type { StockDto } from '@/lib/inventory/stock';

/**
 * Server-side reads for the storefront.
 *
 * Everything here runs on the server and goes through `apiFetch`, so the bearer
 * token stays in the server session. There is deliberately NO client-side fetch
 * module for catalog or basket data: a browser-held token would be a token in
 * localStorage, which is the thing this whole architecture exists to avoid.
 *
 * Caching: catalog reads use a short revalidate window plus tags so a publish
 * can invalidate them. Basket and order reads are `no-store` — they are
 * per-customer and change on every mutation, and a cached basket is a wrong
 * basket.
 */

export interface ProductSearchParams {
  searchTerm?: string;
  categoryId?: string;
  minPrice?: number;
  maxPrice?: number;
  page?: number;
  pageSize?: number;
}

/** GET /api/v1/products — AllowAnonymous. */
export async function searchProducts(
  params: ProductSearchParams = {},
): Promise<PaginatedResponse<ProductListItem>> {
  const query = new URLSearchParams();

  if (params.searchTerm) query.set('searchTerm', params.searchTerm);
  if (params.categoryId) query.set('categoryId', params.categoryId);
  if (params.minPrice !== undefined) query.set('minPrice', String(params.minPrice));
  if (params.maxPrice !== undefined) query.set('maxPrice', String(params.maxPrice));

  const page = Math.max(1, params.page ?? 1);
  // Clamp locally to the server's own bounds (ProductEndpoints: 1..100) rather
  // than sending a value the server will silently rewrite.
  const pageSize = Math.min(
    100,
    Math.max(1, params.pageSize ?? DEFAULT_PAGE_SIZE),
  );
  query.set('page', String(page));
  query.set('pageSize', String(pageSize));

  const suffix = query.toString();
  return apiFetch<PaginatedResponse<ProductListItem>>(`/api/v1/products?${suffix}`, {
    next: { revalidate: 60, tags: ['catalog'] },
  });
}

/** GET /api/v1/products/slug/{slug} — AllowAnonymous. */
export async function getProductBySlug(slug: string): Promise<Product | null> {
  try {
    return await apiFetch<Product>(
      `/api/v1/products/slug/${encodeURIComponent(slug)}`,
      { next: { revalidate: 60, tags: ['catalog', `product:${slug}`] } },
    );
  } catch (cause) {
    // A missing product is a normal 404 that the page turns into notFound().
    // Anything else (500, unreachable) must keep propagating, otherwise a real
    // outage renders as "product not found" and hides the fault.
    if (cause instanceof ApiError && cause.status === 404) return null;
    throw cause;
  }
}

/** GET /api/v1/products/{id} — AllowAnonymous. */
export async function getProductById(id: string): Promise<Product | null> {
  try {
    return await apiFetch<Product>(`/api/v1/products/${encodeURIComponent(id)}`, {
      next: { revalidate: 60, tags: ['catalog', `product:${id}`] },
    });
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 404) return null;
    throw cause;
  }
}

/** GET /api/v1/basket — requires a session; rate limited per user. */
export async function getBasket(): Promise<Basket> {
  return apiFetch<Basket>('/api/v1/basket', { cache: 'no-store' });
}

/**
 * GET /api/v1/inventory/product/{productId} — AllowAnonymous.
 *
 * A missing record is a normal 404 (not every product has a stock row) and
 * resolves to null, which the pages treat as "unknown", never as "out of
 * stock". Anything else propagates: a 500 here must not masquerade as
 * availability in either direction.
 *
 * Cached briefly under the product tag, so a publish invalidates it alongside
 * the product itself. Reservation-time truth still lives server-side — this
 * read informs the buy button, it never promises fulfillment.
 */
export async function getStockByProductId(productId: string): Promise<StockDto | null> {
  try {
    return await apiFetch<StockDto>(
      `/api/v1/inventory/product/${encodeURIComponent(productId)}`,
      { next: { revalidate: 30, tags: ['catalog', `product:${productId}`] } },
    );
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 404) return null;
    throw cause;
  }
}

/**
 * GET /api/v1/categories/ — AllowAnonymous.
 *
 * A longer revalidate than products: categories change rarely, and the tag
 * still allows explicit invalidation. An outage must not render as "no
 * categories" — an empty filter list hides purchasable products — so only a
 * 404 (no such collection) resolves to []; anything else propagates.
 */
export async function getCategories(): Promise<Category[]> {
  try {
    return await apiFetch<Category[]>('/api/v1/categories/', {
      next: { revalidate: 300, tags: ['catalog', 'categories'] },
    });
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 404) return [];
    throw cause;
  }
}

/**
 * POST /api/v1/basket/items — requires a session.
 *
 * `idempotencyKey` should be supplied by the caller when a retry of the same
 * logical add must not double the quantity. Left optional here because
 * apiFetch already mints one per mutation; the caller reuses it across retries.
 */
export async function addBasketItem(
  request: AddBasketItemRequest,
  idempotencyKey?: string,
): Promise<Basket> {
  return apiFetch<Basket>('/api/v1/basket/items', {
    method: 'POST',
    body: request,
    cache: 'no-store',
    idempotencyKey,
  });
}

/** PUT /api/v1/basket/items/{productId} — quantity <= 0 removes the row server-side. */
export async function updateBasketItemQuantity(
  productId: string,
  quantity: number,
  idempotencyKey?: string,
): Promise<Basket> {
  return apiFetch<Basket>(
    `/api/v1/basket/items/${encodeURIComponent(productId)}`,
    { method: 'PUT', body: { quantity }, cache: 'no-store', idempotencyKey },
  );
}

/** DELETE /api/v1/basket/items/{productId} */
export async function removeBasketItem(
  productId: string,
  idempotencyKey?: string,
): Promise<Basket> {
  return apiFetch<Basket>(
    `/api/v1/basket/items/${encodeURIComponent(productId)}`,
    { method: 'DELETE', cache: 'no-store', idempotencyKey },
  );
}

/** DELETE /api/v1/basket — requires a session. Empties the whole basket. */
export async function clearBasket(idempotencyKey?: string): Promise<void> {
  await apiFetch<void>('/api/v1/basket', {
    method: 'DELETE',
    cache: 'no-store',
    idempotencyKey,
  });
}