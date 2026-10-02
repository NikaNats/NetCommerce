import { describe, expect, it } from 'vitest';

import { formatMoney, formatMoneyOrDash } from '@/lib/format/money';

describe('formatMoney', () => {
  it('renders a USD amount with cents', () => {
    expect(formatMoney(1299, 'USD')).toBe('$1,299.00');
  });

  it('renders a fractional amount without losing precision', () => {
    expect(formatMoney(19.99, 'USD')).toBe('$19.99');
  });

  it('renders zero as a real zero, not a dash', () => {
    expect(formatMoney(0, 'USD')).toBe('$0.00');
  });

  it('uses no decimals for a zero-decimal currency', () => {
    expect(formatMoney(1500, 'JPY')).toContain('1,500');
    expect(formatMoney(1500, 'JPY')).not.toContain('.00');
  });

  it('handles a EUR amount', () => {
    expect(formatMoney(10, 'EUR')).toContain('10.00');
  });

  it('marks a MISSING currency rather than printing a bare number', () => {
    // A bare "1299.00" reads as a claim about a price with no unit; "??" makes
    // the missing data visible instead of implying a default.
    expect(formatMoney(1299, null)).toBe('1299.00 ??');
    expect(formatMoney(1299, undefined)).toBe('1299.00 ??');
  });

  it('falls back to amount + code for a malformed ISO currency', () => {
    // Intl throws RangeError on an invalid code; the catalog tile must survive.
    expect(() => formatMoney(5, 'NOT-A-CODE')).not.toThrow();
    expect(formatMoney(5, 'NOT-A-CODE')).toContain('5.00');
  });

  it.each([Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY])(
    'renders %p as an em dash rather than "NaN"',
    (amount) => {
      expect(formatMoney(amount, 'USD')).toBe('—');
    },
  );

  it('is case-insensitive about the currency code', () => {
    expect(formatMoney(5, 'usd')).toBe(formatMoney(5, 'USD'));
  });
});

describe('formatMoneyOrDash', () => {
  it('distinguishes absent from zero', () => {
    expect(formatMoneyOrDash(null, 'USD')).toBe('—');
    expect(formatMoneyOrDash(undefined, 'USD')).toBe('—');
    // The important pair: a blank cell and a zero cell must not look alike.
    expect(formatMoneyOrDash(0, 'USD')).toBe('$0.00');
    expect(formatMoneyOrDash(null, 'USD')).not.toBe(formatMoneyOrDash(0, 'USD'));
  });
});