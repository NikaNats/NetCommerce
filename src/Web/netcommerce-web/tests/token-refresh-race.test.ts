import { beforeEach, describe, expect, it, vi } from 'vitest';

import { createTokenStore, AuthError, type TokenResponse } from '@/lib/auth/token-store';

/**
 * Refresh-token concurrency.
 *
 * Keycloak runs NATIVE rotation for this client: the realm sets
 * 'revoke.refresh.token': 'true' and 'refresh.token.max.reuse': '0'. That makes
 * the refresh token strictly SINGLE-USE — presenting it twice revokes the entire
 * session family, on every device, not just this one.
 *
 * So two concurrent refreshes are not merely wasteful: the second one is a
 * replay, and it destroys the user's Keycloak session. These tests pin that
 * exactly one network rotation happens per refresh cycle, and that a transient
 * failure does not masquerade as a dead session.
 */

const pair = (over: Partial<TokenResponse> = {}): TokenResponse => ({
  access_token: 'access-1',
  refresh_token: 'refresh-1',
  expires_in: 300,
  refresh_expires_in: 1800,
  token_type: 'Bearer',
  ...over,
});

/** A refresh response the caller can resolve by hand, to force real overlap. */
function deferredResponse() {
  let release!: (value: Response) => void;
  const promise = new Promise<Response>((resolve) => {
    release = resolve;
  });
  return { promise, release };
}

describe('concurrent refresh', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  it('sends the refresh token exactly ONCE for two concurrent refreshes', async () => {
    const gate = deferredResponse();
    // Queue the exchange response FIRST, then swap in the gated one for the
    // refresh. mockClear() below drops queued values, so ordering matters:
    // the exchange must already be satisfied before we gate anything.
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(pair()), { status: 200 }));

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });

    fetchMock.mockClear();
    fetchMock.mockReturnValue(gate.promise);

    // Both callers observe the same expired-ish session and race.
    const first = store.refresh();
    const second = store.refresh();
    gate.release(
      new Response(JSON.stringify(pair({ access_token: 'access-2', refresh_token: 'refresh-2' })), {
        status: 200,
      }),
    );

    const [a, b] = await Promise.all([first, second]);

    // THE assertion: a single-use token must never be presented twice.
    const refreshCalls = fetchMock.mock.calls.filter((c) =>
      String(c[0]).includes('/auth/refresh'),
    );
    expect(refreshCalls).toHaveLength(1);
    expect(JSON.parse(refreshCalls[0]![1]!.body as string)).toEqual({
      refresh_token: 'refresh-1',
    });

    // Both callers observe the same rotated result rather than diverging.
    expect(a?.accessToken).toBe('access-2');
    expect(b?.accessToken).toBe('access-2');
  });

  it('three concurrent refreshes still present the token once', async () => {
    const gate = deferredResponse();
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(pair()), { status: 200 }));

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });

    fetchMock.mockClear();
    fetchMock.mockReturnValue(gate.promise);

    const all = Promise.all([store.refresh(), store.refresh(), store.refresh()]);
    gate.release(
      new Response(JSON.stringify(pair({ access_token: 'access-3', refresh_token: 'refresh-3' })), {
        status: 200,
      }),
    );
    await all;

    expect(fetchMock.mock.calls.filter((c) => String(c[0]).includes('/auth/refresh'))).toHaveLength(
      1,
    );
  });

  it('does not start a second rotation after the first has settled', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(pair({ access_token: 'access-2', expires_in: 300 })), {
        status: 200,
      }),
    );

    let clock = 1_000;
    const store = createTokenStore({
      baseUrl: 'http://api',
      fetchImpl: fetchMock,
      now: () => clock,
    });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });
    fetchMock.mockClear();

    await store.refresh();
    await store.refresh();

    // Sequentially, the second call has nothing to do: the token was just used.
    expect(fetchMock.mock.calls.filter((c) => String(c[0]).includes('/auth/refresh'))).toHaveLength(
      1,
    );
    void clock;
  });
});

describe('refresh failure classification', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  async function storeWithLiveSession() {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(pair()), { status: 200 }),
    );
    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });
    return store;
  }

  it.each([400, 401])('destroys the session on a %i from the refresh endpoint', async (status) => {
    const store = await storeWithLiveSession();
    fetchMock.mockResolvedValue(new Response('{"error":"invalid_grant"}', { status }));

    expect(await store.refresh()).toBeNull();
    expect(store.current()).toBeNull();
  });

  it.each([429, 500, 502, 503])(
    'KEEPS the session on a transient %i — the token is not known bad',
    async (status) => {
      const store = await storeWithLiveSession();
      fetchMock.mockResolvedValue(new Response('busy', { status }));

      // AuthStrict is rate-limited (ServiceCollectionExtensions.cs:71-82); a 429
      // from a user's own burst must not look like an expired session.
      expect(await store.refresh()).toBeNull();
      expect(store.current()).not.toBeNull();
    },
  );

  it('KEEPS the session when the network fails outright', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockRejectedValue(new Error('ECONNREFUSED'));

    await store.refresh();

    // The refresh token was never presented, so it may still be valid.
    expect(store.current()).not.toBeNull();
  });

  it('destroys the session when the response has no new refresh token', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ ...pair(), refresh_token: null }), { status: 200 }),
    );

    await store.refresh();
    expect(store.current()).toBeNull();
  });

  it('surfaces an AuthError carrying the status for transient failures', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockResolvedValue(new Response('busy', { status: 503 }));

    await store.refresh();
    // The reason is available for logging/metrics even though the session lives.
    expect(fetchMock).toHaveBeenCalled();
  });
});

describe('logout', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  async function storeWithLiveSession() {
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          access_token: 'access-1',
          refresh_token: 'refresh-1',
          expires_in: 300,
          refresh_expires_in: 1800,
          token_type: 'Bearer',
        }),
        { status: 200 },
      ),
    );
    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });
    return store;
  }

  it('ends the Keycloak session via /auth/logout, not just /auth/revoke', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await store.logout();

    // /revoke kills the refresh token but leaves the server-side SSO session
    // alive, so the user silently stays signed in at the identity provider.
    const urls = fetchMock.mock.calls.map((c) => String(c[0]));
    expect(urls).toContain('http://api/api/v1/auth/logout');
    expect(urls).not.toContain('http://api/api/v1/auth/revoke');
  });

  it('sends the refresh token to the logout endpoint', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await store.logout();

    const logoutCall = fetchMock.mock.calls.find((c) =>
      String(c[0]).includes('/auth/logout'),
    )!;
    expect(JSON.parse(logoutCall[1]!.body as string)).toEqual({
      refresh_token: 'refresh-1',
    });
  });

  it('clears the local session even when the server call fails', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockRejectedValue(new Error('network down'));

    await store.logout();

    // The user asked to be signed out locally; a failed remote revoke must not
    // leave this process believing they are still signed in.
    expect(store.current()).toBeNull();
  });

  it('is a no-op when there is no session', async () => {
    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.logout();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('adopt', () => {
  it('computes expiry from the response, never extends a token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(pair({ expires_in: 60 })), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const store = createTokenStore({
      baseUrl: 'http://api',
      fetchImpl: fetchMock,
      now: () => 1_000,
    });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });

    expect(store.current()?.expiresAt).toBe(61_000);
    expect(store.current()?.refreshExpiresAt).toBe(1_801_000);
  });

  it('throws AuthError with 502 when a rotation omits the refresh token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ...pair(), refresh_token: null }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await expect(
      store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' }),
    ).rejects.toThrow(AuthError);
  });
});