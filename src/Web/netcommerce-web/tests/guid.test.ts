import { describe, expect, it } from 'vitest';

import { isGuid } from '@/lib/validation/guid';

/**
 * The single UUID-shape contract every id guard delegates to
 * (isProductId, isCategoryId, and the checkout idempotency-key fallback).
 * The matrix lives here once — domain tests pin one positive and one
 * negative each through their own names, not the whole table again.
 */
describe('isGuid', () => {
  it('accepts the canonical 8-4-4-4-12 form, any case', () => {
    expect(isGuid('3fa85f64-5717-4562-b3fc-2c963f66afa6')).toBe(true);
    expect(isGuid('3FA85F64-5717-4562-B3FC-2C963F66AFA6')).toBe(true);
  });

  it.each([
    '',
    'not-a-guid',
    '3fa85f6457174562b3fc2c963f66afa6',
    'x'.repeat(36),
    '/../../../etc/passwd',
    'leather-weekend-bag',
    '../admin',
  ])('rejects %s', (value) => {
    expect(isGuid(value)).toBe(false);
  });
});
