/**
 * Server-side token custody for the Next.js BFF layer.
 *
 * ## Why this exists at all
 *
 * The API is already a BFF — `src/Api/Endpoints/Auth/AuthEndpoints.cs:7`
 * declares itself "BFF (Backend for Frontend) authentication endpoints" and
 * `KeycloakTokenProxy` performs the real work. So this store deliberately owns
 * NO independent token logic. It holds the token pair in the server process so
 * a browser never sees it, and delegates every lifecycle operation to the
 * backend.
 *
 * ## The one rule
 *
 * Never mint, extend, or locally "refresh" a token here. Every rotation is a
 * call to POST /api/v1/auth/refresh, which proxies Keycloak's native rotation
 * (`revokeRefreshToken=true`). Under native rotation, replaying a consumed
 * refresh token revokes the *entire* Keycloak session. A second, independent
 * refresher would therefore look like a slow session bug that appears at random.
 *
 * ## Session storage
 *
 * The current in-memory holder is a placeholder for the durable store. It is
 * correct for a single-process dev run, and it is NOT correct for multiple
 * instances — a second replica would not see the session. Swapping the backing
 * store must not change any of the semantics above; see `TokenStore` for the
 * seam where that belongs.
 */

/** Mirrors TokenResponse in src/Api/Endpoints/Auth/AuthModels.cs:79-98. */
export interface TokenResponse {
  access_token: string;
  refresh_token: string | null;
  expires_in: number;
  refresh_expires_in: number;
  token_type: string;
}

export interface Session {
  accessToken: string;
  refreshToken: string;
  /** Epoch ms at which the access token stops being accepted. */
  expiresAt: number;
  refreshExpiresAt: number;
}

export interface TokenStoreOptions {
  baseUrl: string;
  fetchImpl?: typeof fetch;
  now?: () => number;
  /** Refresh this far before expiry so in-flight requests don't race it. */
  refreshSkewMs?: number;
}

const DEFAULT_SKEW_MS = 30_000;

export class AuthError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = 'AuthError';
  }
}

/**
 * Refresh failures that mean the refresh token is definitively unusable.
 *
 * Only these destroy the session. Everything else — a 429 from the AuthStrict
 * rate limiter, a 502 from a gateway, a dropped connection — leaves the token
 * untouched and therefore still valid, so the session survives and the next
 * request can try again. Treating a blip as "logged out" is what turns a brief
 * outage into every user being signed out.
 */
function isTerminalAuthFailure(error: unknown): boolean {
  return error instanceof AuthError && (error.status === 400 || error.status === 401);
}

export interface TokenStore {
  exchangeAuthorizationCode(input: {
    code: string;
    codeVerifier: string;
    redirectUri: string;
  }): Promise<Session>;
  refresh(): Promise<Session | null>;
  logout(): Promise<void>;
  current(): Session | null;
  needsRefresh(): boolean;
  /**
   * Test seam — installs a token pair without a network round trip.
   * Deliberately not part of this interface's production surface; see the note
   * on adoptForTest below.
   */
  adoptForTest?(tokens: TokenResponse): void;
}

export function createTokenStore(options: TokenStoreOptions): TokenStore {
  const { baseUrl } = options;
  const doFetch = options.fetchImpl ?? globalThis.fetch;
  const now = options.now ?? (() => Date.now());
  const skew = options.refreshSkewMs ?? DEFAULT_SKEW_MS;

  let session: Session | null = null;

  /**
   * The single in-flight refresh, if any.
   *
   * The refresh token is SINGLE-USE (Keycloak native rotation, max.reuse=0), so
   * two concurrent rotations present the same token twice and the replay revokes
   * the entire Keycloak session family — every device, not just this one.
   * getSession() runs on every apiFetch, so an RSC page that fires two requests
   * in the same tick is enough to trigger it. Concurrent callers therefore share
   * ONE promise and observe the same rotated result.
   */
  let inFlightRefresh: Promise<Session | null> | null = null;

  /**
   * Refresh tokens that have already been presented to the backend.
   *
   * Bounded by the session's own lifetime: entries are only added on refresh,
   * and a dead session is dropped wholesale by its owner. Kept per-store, which
   * means per-session, so it cannot grow across users.
   */
  const presentedRefreshTokens = new Set<string>();

  async function rotate(current: Session): Promise<Session | null> {
    try {
      const response = await post('/api/v1/auth/refresh', {
        refresh_token: current.refreshToken,
      });
      return adopt((await response.json()) as TokenResponse);
    } catch (error) {
      // Only a definitive rejection means the token is dead. A 429 (the
      // AuthStrict limiter), a 502, or a dropped connection leaves the token
      // unpresented and therefore still usable, so the session survives.
      if (isTerminalAuthFailure(error)) {
        session = null;
      }
      return null;
    }
  }

  function adopt(tokens: TokenResponse): Session {
    // A rotation response with no refresh token means the backend did not hand
    // back a usable pair. Retaining the previous refresh token would mean
    // replaying an already-consumed one, which under native rotation revokes
    // the whole session. Treat it as a dead session instead.
    if (!tokens.refresh_token) {
      session = null;
      throw new AuthError('Token response contained no refresh token', 502);
    }

    const at = now();
    session = {
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token,
      expiresAt: at + tokens.expires_in * 1000,
      refreshExpiresAt: at + tokens.refresh_expires_in * 1000,
    };
    return session;
  }

  async function post(path: string, body: unknown): Promise<Response> {
    const response = await doFetch(`${baseUrl}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      throw new AuthError(`Auth request failed: ${response.status}`, response.status);
    }
    return response;
  }

  return {
    async exchangeAuthorizationCode({ code, codeVerifier, redirectUri }) {
      const response = await post('/api/v1/auth/token', {
        grant_type: 'authorization_code',
        code,
        code_verifier: codeVerifier,
        redirect_uri: redirectUri,
      });
      return adopt((await response.json()) as TokenResponse);
    },

    async refresh() {
      const current = session;
      if (!current) return null;

      // Join the rotation already in flight rather than starting a second one.
      if (inFlightRefresh) return inFlightRefresh;

      // A token that has ALREADY been presented must never be presented again —
      // not just concurrently, but ever. `refresh()` is only ever called when
      // needsRefresh() is true, so after a successful rotation the next call is
      // a genuinely new expiry, and presenting the consumed token then would be
      // a replay. Guarding on concurrency alone is not enough.
      if (presentedRefreshTokens.has(current.refreshToken)) {
        // The token is spent but the session may still be usable; do not destroy
        // it, just refuse to replay. needsRefresh() drives the next attempt.
        return null;
      }

      presentedRefreshTokens.add(current.refreshToken);

      inFlightRefresh = rotate(current).finally(() => {
        inFlightRefresh = null;
      });

      return inFlightRefresh;
    },

    async logout() {
      const current = session;
      session = null;
      if (!current) return;

      try {
        // /auth/logout, NOT /auth/revoke. Revoke only kills the refresh token
        // and leaves the server-side Keycloak session alive, so the user stays
        // silently signed in at the identity provider and the next login is a
        // no-op. Logout revokes AND ends the SSO session (RP-Initiated Logout).
        // Both take the same { refresh_token } body; logout returns 204.
        await doFetch(`${baseUrl}/api/v1/auth/logout`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refresh_token: current.refreshToken }),
        });
      } catch {
        // Local session is already cleared; a failed remote logout must not
        // block the user from being signed out here.
      }
    },

    current(): Session | null {
      return session;
    },

    needsRefresh(): boolean {
      if (!session) return false;
      return now() >= session.expiresAt - skew;
    },

    /**
     * Test seam: install a token pair directly, bypassing the network, so the
     * session-lifecycle tests can exercise an authenticated state without a
     * live Keycloak. OPTIONAL on the interface and absent from the declared
     * TokenStore contract, so no production code path can reach it — an
     * arbitrary-token installer on the real interface is a latent bypass.
     */
    adoptForTest(tokens: TokenResponse): void {
      adopt(tokens);
    },
  };
}
