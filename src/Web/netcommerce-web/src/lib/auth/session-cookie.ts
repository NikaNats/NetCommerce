/**
 * The browser-side session cookie.
 *
 * It carries an OPAQUE SESSION ID and nothing else. The access token, refresh
 * token and role list stay in the server process (see lib/auth/token-store.ts).
 * That is the entire point: an XSS in the storefront can read this cookie only
 * with `document.cookie`, which httpOnly prevents, and even a leaked id is
 * inert without the server-side session it names.
 *
 * ## Why `secure` is NOT derived from NODE_ENV
 *
 * A previous design proposal keyed it off NODE_ENV, and so did an earlier
 * revision of this file. That is wrong in both directions: a production build
 * terminating TLS at a proxy speaks plain HTTP to Node and would never receive
 * the cookie (login loops forever), while a staging deploy over real HTTPS would
 * set a non-Secure cookie that travels in clear. The request's own scheme is
 * the only signal that reflects what the browser actually sees. Production still
 * forces Secure, so a misconfigured proxy fails closed rather than leaking.
 */

export const SESSION_COOKIE = 'nc_session';

/** Short-lived on purpose: it references a server session, it is not a credential. */
export const SESSION_MAX_AGE_SECONDS = 60 * 60 * 8;

/**
 * Decide the Secure flag from the request rather than the build mode.
 *
 * `requestUrl` is the URL the browser used, so it reflects the scheme as seen
 * from outside any TLS-terminating proxy.
 */
export function shouldUseSecureCookie(requestUrl: string, isProduction: boolean): boolean {
  if (isProduction) return true;
  try {
    return new URL(requestUrl).protocol === 'https:';
  } catch {
    // An unparseable URL must not silently downgrade the cookie.
    return isProduction;
  }
}

export function buildSessionCookieOptions(options: { secure: boolean; maxAge?: number }) {
  return {
    httpOnly: true,
    sameSite: 'lax' as const,
    secure: options.secure,
    path: '/',
    maxAge: options.maxAge ?? SESSION_MAX_AGE_SECONDS,
  };
}

export function readSessionId(value: string | undefined): string | undefined {
  return value && value.length > 0 ? value : undefined;
}

export function newSessionId(): string {
  return crypto.randomUUID().replace(/-/g, '') + crypto.randomUUID().replace(/-/g, '');
}
