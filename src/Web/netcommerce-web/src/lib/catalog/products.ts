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
  slug: string | null;
  seoTitle: string | null;
  seoDescription: string | null;
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
  primaryImageUrl: string | null;
  status: string;
  slug: string | null;
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
  slug: string | null;
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