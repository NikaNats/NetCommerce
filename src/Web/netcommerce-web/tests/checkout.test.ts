import { describe, expect, it } from 'vitest';

import {
  hasCheckoutErrors,
  isGuid,
  validateCheckoutInput,
  type CheckoutInput,
} from '@/lib/orders/checkout';

const address = {
  recipientName: 'Ada Lovelace',
  street: '12 Analytical Way',
  city: 'London',
  state: '',
  postalCode: 'E1 6AN',
  country: 'UK',
  phone: '',
};

const valid = (overrides: Partial<CheckoutInput> = {}): CheckoutInput => ({
  customerName: 'Ada Lovelace',
  customerEmail: 'ada@example.com',
  shipping: { ...address },
  billing: { ...address },
  sameAsBilling: true,
  ...overrides,
});

describe('validateCheckoutInput', () => {
  it('accepts a complete submission', () => {
    expect(validateCheckoutInput(valid())).toEqual({});
  });

  it('requires a name and a well-formed email', () => {
    const errors = validateCheckoutInput(
      valid({ customerName: '  ', customerEmail: 'not-an-email' }),
    );
    expect(errors.customerName).toBeTruthy();
    expect(errors.customerEmail).toBeTruthy();
  });

  it('requires the ShippingAddress.Create fields (recipient, street, city, country)', () => {
    const errors = validateCheckoutInput(
      valid({
        shipping: {
          ...address,
          recipientName: '',
          street: ' ',
          city: '',
          country: '',
        },
      }),
    );
    expect(errors['shipping.recipientName']).toBeTruthy();
    expect(errors['shipping.street']).toBeTruthy();
    expect(errors['shipping.city']).toBeTruthy();
    expect(errors['shipping.country']).toBeTruthy();
  });

  it('skips billing validation when billing matches shipping', () => {
    const errors = validateCheckoutInput(valid({ sameAsBilling: true }));
    expect(hasCheckoutErrors(errors)).toBe(false);
  });

  it('validates a separate billing address when provided', () => {
    const errors = validateCheckoutInput(
      valid({
        sameAsBilling: false,
        billing: { ...address, street: '', country: '' },
      }),
    );
    expect(errors['billing.street']).toBeTruthy();
    expect(errors['billing.country']).toBeTruthy();
    expect(errors['shipping.street']).toBeUndefined();
  });
});

describe('isGuid', () => {
  // The matrix lives in guid.test.ts; this pins the re-export contract only:
  // checkout callers get the guard from this module.
  it('accepts a UUID and rejects junk', () => {
    expect(isGuid('3fa85f64-5717-4562-b3fc-2c963f66afa6')).toBe(true);
    expect(isGuid('not-a-guid')).toBe(false);
  });
});
