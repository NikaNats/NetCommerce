/**
 * Category contract, transcribed from the server — not guessed.
 *
 * Sources:
 *   src/Catalog/Application/Categories/DTOs/CategoryDto.cs
 *   src/Api/Endpoints/Catalog/CategoryEndpoints.cs (GET /api/v1/categories/)
 *
 * GET / returns a flat array; hierarchy is expressed through parentCategoryId,
 * not nesting. AllowAnonymous, like the product list.
 */

export interface Category {
  id: string;
  name: string;
  description: string;
  slug: string;
  /** Explicit null on the wire for a top-level category. */
  parentCategoryId?: string | null;
  displayOrder: number;
  isActive: boolean;
  /** Explicit null on the wire when unset. */
  imageUrl?: string | null;
}

import { isGuid } from '@/lib/validation/guid';

/** Categories a customer may browse: active only, in display order. */
export function browsableCategories(categories: readonly Category[]): Category[] {
  return categories
    .filter((category) => category.isActive)
    .slice()
    .sort((a, b) => a.displayOrder - b.displayOrder);
}

/**
 * Is this a category UUID?
 *
 * The catalog filter takes categoryId from the query string — attacker-shaped
 * input by definition. An invalid value is ignored (unfiltered catalog),
 * never forwarded to the API. The pattern itself is owned by
 * lib/validation/guid.ts; this name exists so the call site reads as the
 * domain check it is.
 */
export function isCategoryId(value: string): boolean {
  return isGuid(value);
}
