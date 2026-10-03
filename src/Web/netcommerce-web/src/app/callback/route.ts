import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { Route } from 'next';

import {
  SESSION_COOKIE,
  buildSessionCookieOptions,
  readSessionId,
  shouldUseSecureCookie,
} from '@/lib/auth/session-cookie';
import { readConfig, publicOrigin } from '@/lib/config';
import {
  destroySessionById,
  persistSession,
  registerSession,
} from '@/lib/auth/server-session';
import { isValidPkceValue } from '@/lib/auth/pkce';
import { safeReturnTo } from '@/lib/auth/return-to';

/**
 * Complete the Authorization Code + PKCE flow.
 *
 * The code is exchanged by calling the BACKEND's /api/v1/auth/token. The
 * backend is the BFF and owns the Keycloak conversation; this route never
 * speaks to Keycloak directly and never parses a JWT.
 *
 * The session is registered BEFORE the exchange so the token store that
 * receives the tokens is the same one later requests read from.
 */
export async function GET(request: Request): Promise<never> {
  const url = new URL(request.url);
  const code = url.searchParams.get('code');
  const state = url.searchParams.get('state');
  const providerError = url.searchParams.get('error');

  const store = await cookies();
  const expectedState = readSessionId(store.get('nc_oauth_state')?.value);
  const codeVerifier = readSessionId(store.get('nc_pkce_verifier')?.value);

  // Clear the transient PKCE cookies on every exit path.
  store.delete('nc_pkce_verifier');
  store.delete('nc_oauth_state');

  const returnTo = safeReturnTo(store.get('nc_return_to')?.value);
  store.delete('nc_return_to');

  if (providerError) {
    redirect(`/login?error=${encodeURIComponent(providerError)}`);
  }

  if (!code || !state || !expectedState || state !== expectedState) {
    redirect('/login?error=invalid_state');
  }

  if (!codeVerifier) {
    redirect('/login?error=missing_verifier');
  }

  // The verifier arrives from a cookie. A truncated or tampered value would
  // otherwise surface as an opaque 400 from the token endpoint.
  if (!isValidPkceValue(codeVerifier)) {
    redirect('/login?error=invalid_verifier');
  }

  const config = readConfig();
  const session = await registerSession();

  try {
    await session.tokens.exchangeAuthorizationCode({
      code,
      codeVerifier,
      redirectUri: `${publicOrigin(request.url, config.publicOrigin)}${config.callbackPath}`,
    });
  } catch (cause) {
    console.error('[auth] token exchange failed', cause);
    // Drop the half-created session. Registering it before the exchange means a
    // failure here would otherwise strand an entry holding no tokens, reachable
    // by nobody, until the 8h TTL.
    await destroySessionById(session.id);
    redirect('/login?error=token_exchange_failed');
  }

  // Persist the exchanged pair BEFORE setting the cookie. The cookie is what
  // makes this session reachable, so a cookie set against an unpersisted
  // session would look signed in and 401 on the very next request — the failure
  // only visible after the redirect completes.
  await persistSession(session);

  if (!session.tokens.current()) {
    await destroySessionById(session.id);
    redirect('/login?error=token_exchange_failed');
  }

  store.set(
    SESSION_COOKIE,
    session.id,
    buildSessionCookieOptions({
      secure: shouldUseSecureCookie(request.url, process.env.NODE_ENV === 'production'),
    }),
  );

  redirect((returnTo ?? '/') as Route);
}
