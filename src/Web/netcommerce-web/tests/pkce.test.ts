import { describe, expect, it } from 'vitest';

import { buildAuthorizeUrl, createPkcePair, isValidPkceValue } from '@/lib/auth/pkce';

const ENV = {
  baseUrl: 'http://keycloak:8080',
  realm: 'netcommerce',
  clientId: 'netcommerce-web',
  redirectUri: 'http://localhost:3000/callback',
};

describe('createPkcePair', () => {
  it('produces a verifier that satisfies RFC 7636 length rules (43-128 chars)', () => {
    const { codeVerifier } = createPkcePair();
    expect(codeVerifier.length).toBeGreaterThanOrEqual(43);
    expect(codeVerifier.length).toBeLessThanOrEqual(128);
  });

  it('produces an unreserved-character verifier', () => {
    const { codeVerifier } = createPkcePair();
    expect(codeVerifier).toMatch(/^[A-Za-z0-9\-._~]+$/);
  });

  it('derives a challenge that matches the S256 transform of the verifier', async () => {
    const { codeVerifier, codeChallenge } = createPkcePair();

    // base64url(sha256(verifier)) computed independently in the test.
    const expected = Buffer.from(
      await crypto.subtle.digest('SHA-256', new TextEncoder().encode(codeVerifier)),
    )
      .toString('base64')
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');

    expect(codeChallenge).toBe(expected);
  });

  it('produces a different pair on every call', () => {
    const a = createPkcePair();
    const b = createPkcePair();
    expect(a.codeVerifier).not.toBe(b.codeVerifier);
  });
});

describe('buildAuthorizeUrl', () => {
  it('targets the realm authorize endpoint', () => {
    const url = new URL(buildAuthorizeUrl({ ...ENV, challenge: 'c'.repeat(43), state: 'st' }));
    expect(url.origin + url.pathname).toBe(
      'http://keycloak:8080/realms/netcommerce/protocol/openid-connect/auth',
    );
  });

  it('declares the response type and code challenge method Keycloak expects', () => {
    const url = new URL(buildAuthorizeUrl({ ...ENV, challenge: 'chal', state: 'st' }));
    expect(url.searchParams.get('response_type')).toBe('code');
    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
  });

  it('passes the challenge, state, client id and redirect uri', () => {
    const url = new URL(buildAuthorizeUrl({ ...ENV, challenge: 'chal', state: 'st-123' }));
    expect(url.searchParams.get('code_challenge')).toBe('chal');
    expect(url.searchParams.get('state')).toBe('st-123');
    expect(url.searchParams.get('client_id')).toBe('netcommerce-web');
    expect(url.searchParams.get('redirect_uri')).toBe('http://localhost:3000/callback');
  });

  it('requests the api scope the realm defines', () => {
    const url = new URL(buildAuthorizeUrl({ ...ENV, challenge: 'c', state: 's' }));
    expect(url.searchParams.get('scope')).toBe('openid profile email');
  });
});

describe('isValidPkceValue', () => {
  it('rejects a value too short to be a real S256 digest or verifier', () => {
    expect(isValidPkceValue('short')).toBe(false);
  });

  it('accepts a 43-char base64url value', () => {
    expect(isValidPkceValue('a'.repeat(43))).toBe(true);
  });

  it('accepts the verifier this module actually generates', () => {
    expect(isValidPkceValue(createPkcePair().codeVerifier)).toBe(true);
  });

  it('rejects a verifier containing characters outside the unreserved set', () => {
    expect(isValidPkceValue(`${'a'.repeat(42)}/=`)).toBe(false);
  });

  it('rejects an over-long value', () => {
    expect(isValidPkceValue('a'.repeat(129))).toBe(false);
  });
});
