import { describe, expect, it } from 'vitest';

import {
  buildSessionCookieOptions,
  SESSION_COOKIE,
  readSessionId,
  shouldUseSecureCookie,
} from '@/lib/auth/session-cookie';

/**
 * The browser holds ONLY an opaque session id. No JWT, no refresh token, no
 * role list is readable from JavaScript — a successful XSS yields an id that is
 * useless without the server-side session behind it.
 */

describe('SESSION_COOKIE', () => {
  it('is not a token-bearing name', () => {
    expect(SESSION_COOKIE).toBe('nc_session');
  });
});

describe('buildSessionCookieOptions', () => {
  it('is httpOnly so script cannot read it', () => {
    expect(buildSessionCookieOptions({ secure: true }).httpOnly).toBe(true);
  });

  it('is SameSite=Lax so the session survives a top-level OAuth redirect back', () => {
    // 'strict' would drop the cookie on the cross-site GET back from Keycloak.
    expect(buildSessionCookieOptions({ secure: true }).sameSite).toBe('lax');
  });

  it('is Secure in production and not in development', () => {
    expect(buildSessionCookieOptions({ secure: true }).secure).toBe(true);
    expect(buildSessionCookieOptions({ secure: false }).secure).toBe(false);
  });

  it('scopes the cookie to the whole site and pins path to /', () => {
    const opts = buildSessionCookieOptions({ secure: false });
    expect(opts.path).toBe('/');
  });
});

describe('shouldUseSecureCookie', () => {
  it('follows the request scheme in development, not NODE_ENV', () => {
    expect(shouldUseSecureCookie('https://localhost:3000/callback', false)).toBe(true);
    expect(shouldUseSecureCookie('http://localhost:3000/callback', false)).toBe(false);
  });

  it('sets Secure in production regardless of the request scheme', () => {
    // A proxy terminating TLS speaks HTTP to Node; the browser still needs Secure.
    expect(shouldUseSecureCookie('http://localhost:3000/callback', true)).toBe(true);
  });

  it('does not downgrade on an unparseable URL', () => {
    expect(shouldUseSecureCookie('not a url', true)).toBe(true);
    expect(shouldUseSecureCookie('not a url', false)).toBe(false);
  });
});

describe('readSessionId', () => {
  it('returns undefined when the cookie is absent', () => {
    expect(readSessionId(undefined)).toBeUndefined();
  });

  it('returns the value when present', () => {
    expect(readSessionId('abc123')).toBe('abc123');
  });

  it('treats an empty value as absent', () => {
    expect(readSessionId('')).toBeUndefined();
  });
});
