/**
 * GUID shape validation — the single source of truth.
 *
 * Product ids, category ids, order ids and idempotency keys are all UUIDs on
 * the wire, and every one of them arrives from an attacker-reachable position
 * (query string, hidden form field, route param). Three copies of this pattern
 * used to live in products.ts, categories.ts and checkout.ts, held together
 * only by comments referencing each other — so tightening one would silently
 * diverge validation across the three paths. The pattern lives here now;
 * domain modules keep their own names (isProductId, isCategoryId) because the
 * name at a call site carries domain meaning, but all of them delegate here.
 *
 * Deliberately strict and version-agnostic (the canonical 8-4-4-4-12 form)
 * rather than accepting any 36-character string: a value containing `/`, `..`
 * or a slug would change which route a rebuilt URL resolves to.
 */

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isGuid(value: string): boolean {
  return GUID_PATTERN.test(value);
}
