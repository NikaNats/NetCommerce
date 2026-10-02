import { describe, expect, it } from 'vitest';

import { safeReturnTo } from '@/lib/auth/return-to';

/**
 * Open-redirect protection for the post-login destination.
 *
 * An attacker who controls `returnTo` must not be able to bounce a user —
 * who has just completed a real Keycloak login — to an external site.
 */
describe('safeReturnTo', () => {
  it('accepts a same-origin path', () => {
    expect(safeReturnTo('/orders/123')).toBe('/orders/123');
  });

  it('accepts a path with a query string', () => {
    expect(safeReturnTo('/orders?tab=history')).toBe('/orders?tab=history');
  });

  it('returns null when absent', () => {
    expect(safeReturnTo(null)).toBeNull();
    expect(safeReturnTo(undefined)).toBeNull();
    expect(safeReturnTo('')).toBeNull();
  });

  it('rejects an absolute URL to another host', () => {
    expect(safeReturnTo('https://evil.example/steal')).toBeNull();
    expect(safeReturnTo('http://evil.example')).toBeNull();
  });

  it('rejects a protocol-relative URL', () => {
    // "//evil.example" is an absolute URL with an implied https: scheme.
    expect(safeReturnTo('//evil.example')).toBeNull();
  });

  it('rejects a backslash variant that browsers treat as protocol-relative', () => {
    expect(safeReturnTo('/\\evil.example')).toBeNull();
  });

  it('rejects a percent-encoded protocol-relative URL', () => {
    expect(safeReturnTo('%2F%2Fevil.example')).toBeNull();
  });

  it('rejects a percent-encoded absolute URL', () => {
    expect(safeReturnTo('%2F%2F%2Fevil.example')).toBeNull();
  });

  it('rejects a relative path without a leading slash', () => {
    expect(safeReturnTo('orders/123')).toBeNull();
  });

  it('rejects malformed percent-encoding', () => {
    expect(safeReturnTo('/orders/%zz')).toBeNull();
  });

  it('rejects a control-character smuggling attempt', () => {
    expect(safeReturnTo('/orders\n//evil.example')).toBeNull();
    expect(safeReturnTo('/orders\r\nSet-Cookie: x=1')).toBeNull();
  });

  it('does not redirect to a javascript: URI', () => {
    expect(safeReturnTo('javascript:alert(1)')).toBeNull();
  });
});