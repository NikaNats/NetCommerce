import { describe, expect, it } from 'vitest';

import {
  canPurchaseStock,
  stockState,
  type StockDto,
} from '@/lib/inventory/stock';

/**
 * Mirrors StockDto (Inventory/.../Stock/Queries/StockQueries.cs:8-17) and the
 * reservation reality behind it: the saga fails fast when nothing is
 * available, so a proven-empty record withholds the buy form — while a
 * missing record must never block a purchase the server might fulfill.
 */
const stock = (overrides: Partial<StockDto> = {}): StockDto => ({
  id: 's1',
  productId: 'p1',
  sku: 'SKU-1',
  quantity: 10,
  reservedQuantity: 2,
  availableQuantity: 8,
  lowStockThreshold: 3,
  isLowStock: false,
  lastUpdatedAt: '2026-10-01T00:00:00Z',
  ...overrides,
});

describe('stockState', () => {
  it('is in-stock above the threshold', () => {
    expect(stockState(stock())).toBe('in-stock');
  });

  it('is low-stock when the server says so', () => {
    expect(
      stockState(stock({ availableQuantity: 2, isLowStock: true })),
    ).toBe('low-stock');
  });

  it('is out-of-stock at zero available even when the flag disagrees', () => {
    expect(
      stockState(stock({ availableQuantity: 0, isLowStock: false })),
    ).toBe('out-of-stock');
  });

  it('is unknown without a record — absence of a record is not zero stock', () => {
    expect(stockState(null)).toBe('unknown');
  });
});

describe('canPurchaseStock', () => {
  it('withholds the form only on proven-empty', () => {
    expect(canPurchaseStock(stock())).toBe(true);
    expect(
      canPurchaseStock(stock({ availableQuantity: 1, isLowStock: true })),
    ).toBe(true);
    expect(canPurchaseStock(null)).toBe(true);
    expect(canPurchaseStock(stock({ availableQuantity: 0 }))).toBe(false);
  });
});
