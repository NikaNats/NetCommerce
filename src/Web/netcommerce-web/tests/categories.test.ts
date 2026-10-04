import { describe, expect, it } from 'vitest';

import {
  browsableCategories,
  isCategoryId,
  type Category,
} from '@/lib/catalog/categories';

const category = (overrides: Partial<Category> = {}): Category => ({
  id: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
  name: 'Desks',
  description: 'Work surfaces',
  slug: 'desks',
  parentCategoryId: null,
  displayOrder: 0,
  isActive: true,
  imageUrl: null,
  ...overrides,
});

describe('browsableCategories', () => {
  it('hides inactive categories so unpublished rows never filter the catalog', () => {
    const all = [
      category({ name: 'Live', displayOrder: 1 }),
      category({ name: 'Hidden', displayOrder: 0, isActive: false }),
    ];
    expect(browsableCategories(all).map((c) => c.name)).toEqual(['Live']);
  });

  it('orders by displayOrder, the server-defined sequence', () => {
    const all = [
      category({ name: 'B', displayOrder: 2 }),
      category({ name: 'A', displayOrder: 1 }),
    ];
    expect(browsableCategories(all).map((c) => c.name)).toEqual(['A', 'B']);
  });
});

describe('isCategoryId', () => {
  // The matrix lives in guid.test.ts; this pins the domain contract only.
  it('accepts a UUID and rejects a non-id', () => {
    expect(isCategoryId('3fa85f64-5717-4562-b3fc-2c963f66afa6')).toBe(true);
    expect(isCategoryId('../admin')).toBe(false);
  });
});
