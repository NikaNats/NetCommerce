/**
 * Catalog contract, transcribed from the server — not guessed.
 *
 * Sources (read before writing this file):
 *   src/Catalog/Application/Products/DTOs/ProductDto.cs
 *   src/Api/Endpoints/Common/PaginatedResponse.cs
 *   src/Api/Endpoints/Catalog/ProductEndpoints.cs
 *
 * Route prefix is `/api/v{version:apiVersion}/products` with `HasApiVersion(1.0)`,
 * so the concrete path is `/api/v1/products`.
 */

export interface ProductImage {
  id: string;
  url: string;
  displayOrder: number;
  isPrimary: boolean;
}

export interface ProductAttribute {
  key: string;
  value: string;
  displayName: string;
}

/** GET /api/v1/products/slug/{slug} */
export interface Product {
  id: string;
  name: string;
  description: string;
  sku: string;
  price: number;
  currency: string;
  categoryId: string;
  categoryName: string;
  status: string;
  /**
   * Nullable AND optional: the API emits an explicit JSON null for these
   * (observed live 2026-10-03: `"seoTitle":null`, and `"slug"` present on the
   * detail DTO). An earlier revision declared these `?: string` alone, based on
   * an isolated run of the server's serializer options (ApiJsonContext sets
   * DefaultIgnoreCondition = WhenWritingNull) — but the live HTTP path goes
   * through the shared HttpJsonOptions, where that ignore rule is not in
   * effect, so nulls arrive as null. Every consumer below treats them by
   * truthiness, which handles null and undefined identically; the `| null`
   * keeps the TYPE honest about what the wire actually produces.
   */
  slug?: string | null;
  seoTitle?: string | null;
  seoDescription?: string | null;
  images: ProductImage[];
  attributes: ProductAttribute[];
}

/**
 * GET /api/v1/products?page=&pageSize=
 *
 * NOTE the field asymmetry with Product: the list projection carries
 * `primaryImageUrl` and NO description, category, images or attributes. Building
 * a product card from `Product` fields would render blanks that look like a bug.
 */
export interface ProductListItem {
  id: string;
  name: string;
  sku: string;
  price: number;
  currency: string;
  /** Explicit null on the wire when unset (see Product.slug). */
  primaryImageUrl?: string | null;
  status: string;
  /** Explicit null on the wire when the product has no slug. */
  slug?: string | null;
}

export interface PaginationMetadata {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface PaginatedResponse<T> {
  items: T[];
  pagination: PaginationMetadata;
}

/** Server clamps pageSize to 1..100 and defaults page to 1. */
export const DEFAULT_PAGE_SIZE = 20;
export const MAX_PAGE_SIZE = 100;

/**
 * Canonical detail path for a product.
 *
 * Prefers `slug` because the server has a dedicated slug route; falls back to the
 * id route. Returns null only when the server sent neither, which is the one case
 * where a card genuinely cannot link anywhere.
 */
export function productHref(product: {
  slug?: string | null;
  id: string;
}): string | null {
  if (product.slug) return `/products/${encodeURIComponent(product.slug)}`;
  if (product.id) return `/products/id/${encodeURIComponent(product.id)}`;
  return null;
}

/** Primary image, falling back to the lowest displayOrder. Mirrors server intent. */
export function primaryImage(images: readonly ProductImage[]): ProductImage | null {
  if (images.length === 0) return null;
  const flagged = images.find((image) => image.isPrimary);
  if (flagged) return flagged;
  return images.reduce((best, candidate) =>
    candidate.displayOrder < best.displayOrder ? candidate : best,
  );
}

/**
 * Products are only shown to customers once published. The server filters this
 * itself; this exists so a stray non-published row cannot be rendered as
 * purchasable from a projection that did not filter.
 */
export function isPublished(status: string): boolean {
  return status.toLowerCase() === 'published';
}

/**
 * Is this a product UUID?
 *
 * ## Why the shape is checked
 *
 * `/products/[slug]` and `/products/id/[id]` are separate routes, so anything that
 * builds a product URL from an ID MUST use the `/id/` segment — a bare
 * `/products/<uuid>` resolves through the slug route and 404s.
 *
 * This guard makes that mistake loud instead of silent. It is deliberately
 * strict and version-agnostic (the canonical 8-4-4-4-12 form) rather than
 * accepting any 36-character string: `returnTo` is attacker-reachable through the
 * login query string, and a value containing `/`, `..`, or a slug would change
 * which route the post-login redirect resolves.
 */
const PRODUCT_ID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isProductId(value: string): boolean {
  return PRODUCT_ID_PATTERN.test(value);
}

/** The canonical detail URL for a product ID. Always use this, never string-build. */
export function productIdPath(id: string): string {
  if (!isProductId(id)) {
    throw new Error(`refusing to build a product URL from a non-UUID id: ${id}`);
  }
  return `/products/id/${id}`;
}