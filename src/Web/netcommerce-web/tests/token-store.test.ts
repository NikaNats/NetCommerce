import { beforeEach, describe, expect, it, vi } from 'vitest';

import { createTokenStore, type TokenResponse } from '@/lib/auth/token-store';

/**
 * Boundary under test (user decision): Next.js holds tokens server-side, but
 * EVERY rotation goes through the backend's POST /api/v1/auth/refresh.
 * Next.js must never mint, extend, or locally refresh a token itself, because
 * Keycloak runs native rotation (revokeRefreshToken=true) where replaying an
 * old refresh token revokes the whole session
 * (src/Api/Endpoints/Auth/AuthEndpoints.cs:47-50).
 */

const tokenResponse = (over: Partial<TokenResponse> = {}): TokenResponse => ({
  access_token: 'access-1',
  refresh_token: 'refresh-1',
  expires_in: 300,
  refresh_expires_in: 1800,
  token_type: 'Bearer',
  ...over,
});

describe('token store', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  /** A store holding a live session, without touching the network twice. */
  async function storeWithLiveSession() {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(tokenResponse()), { status: 200 }));
    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });
    return store;
  }

  it('stores tokens returned by the backend token exchange', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(tokenResponse()), { status: 200 }),
    );

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    const tokens = await store.exchangeAuthorizationCode({
      code: 'auth-code',
      codeVerifier: 'verifier',
      redirectUri: 'http://localhost:3000/callback',
    });

    expect(tokens.accessToken).toBe('access-1');
    expect(store.current()?.accessToken).toBe('access-1');
  });

  it('posts snake_case fields the API actually binds', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(tokenResponse()), { status: 200 }),
    );

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({
      code: 'auth-code',
      codeVerifier: 'verifier',
      redirectUri: 'http://localhost:3000/callback',
    });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe('http://api/api/v1/auth/token');

    const body = JSON.parse((init as RequestInit).body as string);
    expect(body).toMatchObject({
      grant_type: 'authorization_code',
      code: 'auth-code',
      code_verifier: 'verifier',
      redirect_uri: 'http://localhost:3000/callback',
    });
  });

  it('delegates refresh to the backend and adopts the rotated pair', async () => {
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify(tokenResponse()), { status: 200 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify(tokenResponse({ access_token: 'access-2', refresh_token: 'refresh-2' })), {
          status: 200,
        }),
      );

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({
      code: 'c',
      codeVerifier: 'v',
      redirectUri: 'http://localhost:3000/callback',
    });

    const refreshed = await store.refresh();

    expect(refreshed?.accessToken).toBe('access-2');
    const [url, init] = fetchMock.mock.calls[1]!;
    expect(url).toBe('http://api/api/v1/auth/refresh');
    expect(JSON.parse((init as RequestInit).body as string)).toEqual({
      refresh_token: 'refresh-1',
    });
  });

  it('clears the session when the backend rejects the refresh', async () => {
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify(tokenResponse()), { status: 200 }))
      .mockResolvedValueOnce(new Response('{"error":"invalid_grant"}', { status: 401 }));

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });

    const refreshed = await store.refresh();

    expect(refreshed).toBeNull();
    expect(store.current()).toBeNull();
  });

  it('refuses to refresh when the response carries no new refresh token', async () => {
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify(tokenResponse()), { status: 200 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify(tokenResponse({ refresh_token: null })), { status: 200 }),
      );

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });

    // Silently keeping the old refresh token would replay a rotated token on the
    // next call, which under native rotation kills the whole Keycloak session.
    expect(await store.refresh()).toBeNull();
    expect(store.current()).toBeNull();
  });

  it('never synthesizes a token locally when the backend is unreachable', async () => {
    fetchMock.mockRejectedValue(new Error('ECONNREFUSED'));

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock });

    await expect(
      store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' }),
    ).rejects.toThrow();
    expect(store.current()).toBeNull();
  });

  it('ends the Keycloak session via /auth/logout', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await store.logout();

    const [url, init] = fetchMock.mock.calls[1]!;
    // Logout ends the SSO session server-side; /revoke would only kill the
    // refresh token and leave the user silently signed in at Keycloak.
    expect(url).toBe('http://api/api/v1/auth/logout');
    expect(JSON.parse(init!.body as string)).toEqual({ refresh_token: 'refresh-1' });
    expect(store.current()).toBeNull();
  });

  it('does not throw when the logout call fails', async () => {
    const store = await storeWithLiveSession();
    fetchMock.mockRejectedValue(new Error('network down'));

    await expect(store.logout()).resolves.toBeUndefined();
    expect(store.current()).toBeNull();
  });

  it('exposes expiry so callers can refresh before a request fails', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(tokenResponse({ expires_in: 300 })), { status: 200 }),
    );

    const store = createTokenStore({ baseUrl: 'http://api', fetchImpl: fetchMock, now: () => 1_000 });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });

    expect(store.current()?.expiresAt).toBe(1_000 + 300_000);
  });

  it('reports whether the access token is within the refresh skew window', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify(tokenResponse({ expires_in: 300 })), { status: 200 }),
    );

    let clock = 1_000;
    const store = createTokenStore({
      baseUrl: 'http://api',
      fetchImpl: fetchMock,
      now: () => clock,
      refreshSkewMs: 30_000,
    });
    await store.exchangeAuthorizationCode({ code: 'c', codeVerifier: 'v', redirectUri: 'u' });

    // expiresAt = 1000 + 300_000 = 301_000. Refresh triggers at 271_000.
    clock = 1_000; // just issued
    expect(store.needsRefresh()).toBe(false);

    clock = 270_000; // 31s of life left — outside the skew window
    expect(store.needsRefresh()).toBe(false);

    clock = 271_000; // exactly 30s left — boundary
    expect(store.needsRefresh()).toBe(true);

    clock = 280_000; // 21s of life left — inside the skew window
    expect(store.needsRefresh()).toBe(true);
  });
});
