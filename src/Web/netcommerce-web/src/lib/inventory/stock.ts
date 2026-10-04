/**
 * Stock contract, transcribed from the server — not guessed.
 *
 * Sources:
 *   src/Inventory/Inventory.Application/Stock/Queries/StockQueries.cs
 *     (StockDto, GetStockByProductIdQuery)
 *   src/Api/Endpoints/Inventory/InventoryEndpoints.cs:19-22
 *     (GET /api/v1/inventory/product/{productId}, AllowAnonymous)
 *
 * A single state value instead of three booleans: availability, low-stock and
 * unknown-ness are mutually exclusive presentations, and separate flags would
 * let a caller render "in stock" next to a disabled buy button. The states:
 *
 *   unknown      — no stock record (404) or the read failed. NOT "out of
 *                  stock": absence of a record says nothing about inventory,
 *                  and the reservation saga remains authoritative.
 *   in-stock     — availableQuantity > 0 and above the low-stock threshold.
 *   low-stock    — server-computed IsLowStock (available at/below threshold).
 *   out-of-stock — availableQuantity <= 0. An order would fail at reservation,
 *                  so the buy form is withheld, not left to fail.
 */

export interface StockDto {
  id: string;
  productId: string;
  sku: string;
  quantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  lowStockThreshold: number;
  isLowStock: boolean;
  lastUpdatedAt: string;
}

export type StockState = 'unknown' | 'in-stock' | 'low-stock' | 'out-of-stock';

export function stockState(stock: StockDto | null): StockState {
  if (!stock) return 'unknown';
  if (stock.availableQuantity <= 0) return 'out-of-stock';
  return stock.isLowStock ? 'low-stock' : 'in-stock';
}

/**
 * Whether the buy form may be offered.
 *
 * Everything except proven-empty: an unknown record must not block a purchase
 * the server might still fulfill — the saga fails fast at reservation when
 * stock truly is gone.
 */
export function canPurchaseStock(stock: StockDto | null): boolean {
  return stockState(stock) !== 'out-of-stock';
}
