/**
 * PKCE (RFC 7636) support for the Authorization Code flow.
 *
 * The authorization code is exchanged by the BACKEND, not here — this module
 * only produces the verifier/challenge pair and the Keycloak authorize URL.
 * `code_verifier` is handed to POST /api/v1/auth/token, which proxies the
 * exchange to Keycloak via KeycloakTokenProxy.
 */

import { createHash } from 'node:crypto';

export interface PkceEnv {
  baseUrl: string;
  realm: string;
  clientId: string;
  redirectUri: string;
}

export interface PkcePair {
  codeVerifier: string;
  codeChallenge: string;
}

function base64url(bytes: Uint8Array): string {
  return Buffer.from(bytes)
    .toString('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

/**
 * Create a verifier/challenge pair using S256.
 *
 * The verifier is 64 random bytes rendered base64url (86 chars) — inside the
 * RFC's 43-128 range, and composed only of unreserved characters.
 */
export function createPkcePair(): PkcePair {
  const codeVerifier = base64url(crypto.getRandomValues(new Uint8Array(64)));

  // `createHash` from node:crypto is synchronous and avoids making this async
  // purely to compute a digest.
  const codeChallenge = base64url(
    new Uint8Array(createHash('sha256').update(codeVerifier).digest()),
  );

  return { codeVerifier, codeChallenge };
}

/**
 * Validate a PKCE code_verifier read back from a cookie.
 *
 * An S256 challenge is always exactly 43 base64url characters. A verifier is
 * 43-128 unreserved characters. Both satisfy this check, which is what we need
 * before handing a cookie-supplied value to the token endpoint: a truncated or
 * tampered cookie would otherwise become a confusing 400 from Keycloak.
 */
export function isValidPkceValue(value: string): boolean {
  return /^[A-Za-z0-9\-._~]{43,128}$/.test(value);
}

export function buildAuthorizeUrl(
  env: PkceEnv & { challenge: string; state: string },
): string {
  const url = new URL(
    `${env.baseUrl.replace(/\/$/, '')}/realms/${env.realm}/protocol/openid-connect/auth`,
  );

  url.searchParams.set('client_id', env.clientId);
  url.searchParams.set('response_type', 'code');
  url.searchParams.set('scope', 'openid profile email');
  url.searchParams.set('redirect_uri', env.redirectUri);
  url.searchParams.set('code_challenge', env.challenge);
  url.searchParams.set('code_challenge_method', 'S256');
  url.searchParams.set('state', env.state);

  return url.toString();
}
