import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Session lifecycle against the pluggable store.
 *
 * Runs on the MemorySessionStore — the development path. The Redis path is
 * covered in redis-session-store.test.ts; what is verified HERE is the lifecycle
 * logic in server-session.ts, which is identical for both.
 *
 * ## Why these tests stub fetch, not TokenStore methods
 *
 * getSession() rehydrates a FRESH TokenStore from persisted state on every call,
 * so a `session.tokens.refresh = vi.fn()` installed on the object returned by
 * registerSession() is discarded before it can ever run. Stubbing
 * globalThis.fetch is the seam that survives rehydration — it is what the store
 * actually calls. An earlier version of this file stubbed the methods, and five
 * tests failed for exactly that reason.
 */

const cookieJar = new Map<string, string>();

vi.mock('next/headers', () => ({
  cookies: async () => ({
    get: (name: string) =>
      cookieJar.has(name) ? { name, value: cookieJar.get(name)! } : undefined,
    set: (name: string, value: string) => cookieJar.set(name, value),
    delete: (name: string) => cookieJar.delete(name),
  }),
}));

const {
  __registrySize,
  __resetRegistryForTests,
  getSession,
  getSessionById,
  persistSession,
  registerSession,
  SESSION_ABSOLUTE_TTL_MS,
} = await import('@/lib/auth/server-session');
const { SESSION_COOKIE } = await import('@/lib/auth/session-cookie');
const { __setSessionStoreForTests, getSessionStore } = await import(
  '@/lib/auth/session-store-factory'
);
const { MemorySessionStore } = await import('@/lib/auth/session-store');

let clock = 1_000_000;

/** Advance the mocked system clock (not just a local variable). */
function advance(ms: number) {
  clock += ms;
  vi.setSystemTime(clock);
}

/**
 * Stub the auth upstream.
 *
 * `presented` counts how many times a refresh token was PRESENTED, which is the
 * number that matters: Keycloak revokes the whole session family if the same
 * single-use token is presented twice.
 */
function stubAuth(options: {
  expiresIn?: number;
  refreshExpiresIn?: number;
  /** Return 401 to simulate a dead refresh token. */
  rejectRefresh?: boolean;
} = {}) {
  const state = { presented: 0, refreshCalls: 0 };

  const tokenResponse = (suffix: string) =>
    new Response(
      JSON.stringify({
        access_token: `access-${suffix}`,
        refresh_token: `refresh-${suffix}`,
        expires_in: options.expiresIn ?? 300,
        refresh_expires_in: options.refreshExpiresIn ?? 1800,
        token_type: 'Bearer',
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    );

  vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
    const url = String(input);

    if (url.includes('/auth/token')) return tokenResponse('1');

    if (url.includes('/auth/refresh')) {
      state.refreshCalls += 1;
      state.presented += 1;
      if (options.rejectRefresh) return new Response('{}', { status: 401 });
      return tokenResponse(`${state.refreshCalls + 1}`);
    }

    if (url.includes('/auth/logout')) return new Response(null, { status: 204 });

    return new Response('{}', { status: 404 });
  });

  return state;
}

/** Register a session, exchange a code so real tokens exist, set the cookie. */
async function authenticatedSession() {
  const session = await registerSession();
  await session.tokens.exchangeAuthorizationCode({
    code: 'code-1',
    codeVerifier: 'verifier-1',
    redirectUri: 'http://localhost:3000/callback',
  });
  await persistSession(session);
  cookieJar.set(SESSION_COOKIE, session.id);
  return session;
}

beforeEach(() => {
  cookieJar.clear();
  // A fresh store per test: the singleton would otherwise leak sessions between
  // cases, which is how a suite passes for the wrong reason.
  __setSessionStoreForTests(new MemorySessionStore());
  clock = 1_000_000;
  vi.useFakeTimers();
  vi.setSystemTime(clock);
});

afterEach(async () => {
  await __resetRegistryForTests();
  __setSessionStoreForTests(undefined);
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('registerSession', () => {
  it('creates a session with a long opaque id', async () => {
    const session = await registerSession();
    expect(session.id.length).toBeGreaterThanOrEqual(64);
  });

  it('gives each session a distinct id', async () => {
    const ids = new Set<string>();
    for (let i = 0; i < 25; i += 1) {
      ids.add((await registerSession()).id);
    }
    expect(ids.size).toBe(25);
  });

  it('persists the session so another request can find it', async () => {
    const session = await registerSession();
    expect((await getSessionById(session.id))?.id).toBe(session.id);
  });
});

describe('getSessionById', () => {
  it('returns undefined for no id', async () => {
    expect(await getSessionById(undefined)).toBeUndefined();
  });

  it('returns undefined for an unknown id', async () => {
    expect(await getSessionById('does-not-exist')).toBeUndefined();
  });

  it('rehydrates the token pair from the store', async () => {
    stubAuth();
    const session = await authenticatedSession();
    const found = await getSessionById(session.id);
    expect(found?.tokens.current()?.accessToken).toBe('access-1');
  });

  it('drops a session past the absolute TTL', async () => {
    stubAuth();
    const session = await authenticatedSession();
    advance(SESSION_ABSOLUTE_TTL_MS + 1_000);
    expect(await getSessionById(session.id)).toBeUndefined();
  });
});

describe('getSession', () => {
  it('returns undefined with no session cookie', async () => {
    expect(await getSession()).toBeUndefined();
  });

  it('returns the session when the cookie matches', async () => {
    stubAuth();
    await authenticatedSession();
    expect(await getSession()).toBeDefined();
  });

  it('returns undefined when the cookie names an unknown session', async () => {
    cookieJar.set(SESSION_COOKIE, 'nope');
    expect(await getSession()).toBeUndefined();
  });

  it('rejects a session whose refresh token has expired upstream', async () => {
    // An 8h local ceiling over a 30-minute refresh token is fiction; the real
    // bound is upstream. Without this check the session looks valid while every
    // API call 401s.
    stubAuth({ refreshExpiresIn: 1 });
    const session = await authenticatedSession();

    advance(5_000);

    expect(await getSession()).toBeUndefined();
    expect(await getSessionById(session.id)).toBeUndefined();
  });

  it('enforces the absolute TTL even when refresh keeps succeeding', async () => {
    stubAuth();
    const session = await authenticatedSession();

    // Refresh always succeeds, so only the ceiling can end this session.
    for (let i = 0; i < 5; i += 1) {
      advance(SESSION_ABSOLUTE_TTL_MS / 2);
      await getSession();
    }

    expect(await getSession()).toBeUndefined();
    expect(await getSessionById(session.id)).toBeUndefined();
  });

  it('evicts a session whose refresh was rejected upstream', async () => {
    stubAuth({ rejectRefresh: true });
    const session = await authenticatedSession();

    advance(299_000); // inside the 30s skew window

    expect(await getSession()).toBeUndefined();
    expect(await getSessionById(session.id)).toBeUndefined();
  });

  it('persists a rotated token pair so a later request still works', async () => {
    stubAuth();
    const session = await authenticatedSession();

    advance(299_000); // inside the 30s skew window
    await getSession();

    // A fresh read must see the ROTATED pair: this is what lets the session
    // survive on a replica that never saw the rotation happen.
    const reloaded = await getSessionById(session.id);
    expect(reloaded?.tokens.current()?.refreshToken).not.toBe('refresh-1');
  });

  it('records the spent refresh token so it can never be presented again', async () => {
    stubAuth();
    const session = await authenticatedSession();

    advance(299_000);
    await getSession();

    const stored = await getSessionStore().get(session.id);
    expect(stored?.presentedRefreshTokens).toContain('refresh-1');
  });

  /**
   * The property that makes this safe across replicas.
   *
   * Two requests arrive together, both see an expiring access token, and both
   * want to rotate the SAME single-use refresh token. Without the store's lock the
   * second presentation replays it and Keycloak revokes the entire session
   * family — every device, not just this one.
   */
  it('presents a refresh token at most once under concurrent requests', async () => {
    const state = stubAuth();
    const session = await authenticatedSession();

    advance(299_000); // every caller now needs a refresh

    await Promise.all([getSession(), getSession(), getSession()]);

    // At most ONE rotation, no matter how many callers raced.
    expect(state.presented).toBeLessThanOrEqual(1);
  });
});

describe('registry housekeeping', () => {
  it('does not grow without bound once sessions expire', async () => {
    for (let i = 0; i < 50; i += 1) await registerSession();
    expect(await __registrySize()).toBe(50);

    advance(SESSION_ABSOLUTE_TTL_MS + 1_000);
    // Any subsequent access discards the expired entries.
    await getSessionById('anything');

    expect(await __registrySize()).toBe(0);
  });
});