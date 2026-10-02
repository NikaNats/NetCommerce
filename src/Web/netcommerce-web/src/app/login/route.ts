import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { Route } from 'next';

import { readConfig, publicOrigin } from '@/lib/config';
import { buildAuthorizeUrl, createPkcePair } from '@/lib/auth/pkce';
import { shouldUseSecureCookie } from '@/lib/auth/session-cookie';
import { safeReturnTo } from '@/lib/auth/return-to';

/**
 * Kick off the Authorization Code + PKCE flow.
 *
 * The verifier and state are parked in short-lived httpOnly cookies; the code
 * is exchanged by the backend in /callback. Nothing token-shaped is ever
 * exposed to the browser.
 */
export async function GET(request: Request): Promise<never> {
  const config = readConfig();
  const origin = publicOrigin(request.url, config.publicOrigin);
  const { codeVerifier, codeChallenge } = createPkcePair();
  const state = crypto.randomUUID();

  const store = await cookies();
  const secure = shouldUseSecureCookie(request.url, process.env.NODE_ENV === 'production');

  // Honor the post-login destination, but ONLY as a same-site path. An absolute
  // URL here would be an open redirect: an attacker sends a victim to
  // /login?returnTo=https://evil.example and the storefront bounces them there
  // with the login flow completed.
  const returnTo = safeReturnTo(new URL(request.url).searchParams.get('returnTo'));

  const transient = {
    httpOnly: true,
    sameSite: 'lax' as const,
    secure,
    path: '/',
    maxAge: 600,
  };
  store.set('nc_pkce_verifier', codeVerifier, transient);
  store.set('nc_oauth_state', state, transient);
  if (returnTo) store.set('nc_return_to', returnTo, transient);

  const authorizeUrl = buildAuthorizeUrl({
    baseUrl: config.keycloakBaseUrl,
    realm: config.keycloakRealm,
    clientId: config.keycloakClientId,
    redirectUri: `${origin}${config.callbackPath}`,
    challenge: codeChallenge,
    state,
  });

  // typedRoutes narrows redirect() to known app routes; the Keycloak authorize
  // URL is an external origin and is legitimately outside that set.
  redirect(authorizeUrl as Route);
}
