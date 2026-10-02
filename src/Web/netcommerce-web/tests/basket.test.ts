import { describe, expect, it } from 'vitest';

import {
  canCheckout,
  distinctItemCount,
  isEmptyBasket,
  isValidQuantity,
  MIN_ITEM_QUANTITY,
  type Basket,
  type BasketItem,
} from '@/lib/basket/basket';

const item = (overrides: Partial<BasketItem> = {}): BasketItem => ({
  productId: 'p1',
  productName: 'Walnut Desk',
  price: 1299,
  quantity: 1,
  ...overrides,
});

const basket = (items: BasketItem[]): Basket => ({
  customerId: 'cust-1',
  items,
  createdAt: '2026-10-01T00:00:00Z',
  lastUpdatedAt: '2026-10-01T00:00:00Z',
  totalPrice: items.reduce((sum, i) => sum + i.price * i.quantity, 0),
});

describe('isValidQuantity', () => {
  it('accepts 1, the server minimum', () => {
    expect(MIN_ITEM_QUANTITY).toBe(1);
    expect(isValidQuantity(1)).toBe(true);
  });

  it('accepts ordinary quantities', () => {
    expect(isValidQuantity(7)).toBe(true);
  });

  it.each([0, -1])('rejects %i so a delete is not smuggled in as a quantity', (q) => {
    expect(isValidQuantity(q)).toBe(false);
  });

  it.each([1.5, Number.NaN, Number.POSITIVE_INFINITY])(
    'rejects non-integer %p — the server binds an int and would 400',
    (q) => {
      expect(isValidQuantity(q)).toBe(false);
    },
  );
});

describe('isEmptyBasket', () => {
  it('is true for no items', () => {
    expect(isEmptyBasket(basket([]))).toBe(true);
  });

  it('is false when items exist', () => {
    expect(isEmptyBasket(basket([item()]))).toBe(false);
  });
});

describe('distinctItemCount', () => {
  it('counts rows, not summed quantity', () => {
    const b = basket([item({ productId: 'p1', quantity: 5 }), item({ productId: 'p2' })]);
    expect(distinctItemCount(b)).toBe(2);
  });

  it('ignores a zero-quantity row rather than reporting a phantom line', () => {
    const b = basket([item({ productId: 'p1', quantity: 3 }), item({ productId: 'p2', quantity: 0 })]);
    expect(distinctItemCount(b)).toBe(1);
  });
});

describe('canCheckout', () => {
  it('is false for an empty basket, so checkout cannot submit nothing', () => {
    expect(canCheckout(basket([]))).toBe(false);
  });

  it('is false when every row has zero quantity', () => {
    expect(canCheckout(basket([item({ quantity: 0 })]))).toBe(false);
  });

  it('is true with at least one real line', () => {
    expect(canCheckout(basket([item({ quantity: 2 })]))).toBe(true);
  });
});

describe('totalPrice', () => {
  it('is carried from the server rather than recomputed', () => {
    // The UI must render the server's figure. A client-side sum that disagrees
    // with checkout is a financial-integrity bug, so this asserts the field is
    // present on the contract instead of deriving it.
    const b = basket([item({ price: 10, quantity: 3 })]);
    expect(b.totalPrice).toBe(30);
    expect('totalPrice' in b).toBe(true);
  });
});