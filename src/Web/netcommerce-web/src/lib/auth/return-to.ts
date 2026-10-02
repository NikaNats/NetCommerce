import { describe, expect, it } from 'vitest';

/**
 * Post-login destination handling.
 *
 * `returnTo` is attacker-controlled: it arrives in a query string on a link a
 * user may click. Honouring an absolute URL would make this an open redirect —
 * the victim completes a legitimate Keycloak login and is then handed to an
 * attacker's site, with the login flow lending credibility to the bounce.
 */

const SAFE_PREFIX = '/';

/**
 * Accept only a same-origin absolute PATH.
 *
 * Rejects: absolute URLs, protocol-relative URLs (//evil.example), backslash
 * variants that browsers normalise to //, and anything not starting with a
 * single slash. Returns null when the input is unusable, so the caller falls
 * back to a known-good destination.
 */
export function safeReturnTo(raw: string | null | undefined): string | null {
  if (!raw) return null;

  // Decode once: %2F%2Fevil.example decodes to //evil.example.
  let candidate = raw;
  try {
    candidate = decodeURIComponent(raw);
  } catch {
    return null; // malformed percent-encoding
  }

  if (!candidate.startsWith(SAFE_PREFIX)) return null;
  // "//host" and "/\host" are both treated as protocol-relative by browsers.
  if (candidate.startsWith('//')) return null;
  if (candidate.startsWith('/\\')) return null;
  // Control characters can be used to smuggle a second header/URL past a naive
  // prefix check; reject anything non-printable rather than trying to normalise.
  // eslint-disable-next-line no-control-regex
  if (/[\x00-\x1f\x7f]/.test(candidate)) return null;

  return candidate;
}