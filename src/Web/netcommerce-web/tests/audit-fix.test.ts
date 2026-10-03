import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Regression tests for the audit findings on the session rotation path.
 *
 * Each of these FAILED before the corresponding fix, and each is written against
 * the real module rather than a restatement of the fix.
 *
 * Findings covered:
 *   HIGH 3  — a transient upstream failure (429/502/socket) destroyed the session
 *   HIGH 4  — a lock loser was handed a session whose access token was already
 *             inside the refresh window
 *   LOW 7   — a stored record missing presentedRefreshTokens threw a TypeError
 *             instead of degrading to "absent"
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

const { getSession, persistSession, registerSession, SESSION_ABSOLUTE_TTL_MS } =
  await import('@/lib/auth/server-session');
const { SESSION_COOKIE } = await import('@/lib/auth/session-cookie');
const { __setSessionStoreForTests, getSessionStore } = await import(
  '@/lib/auth/session-store-factory'
);
const { MemorySessionStore } = await import('@/lib/auth/session-store');

let clock = 1_000_000;

function advance(ms: number) {
  clock += ms;
  vi.setSystemTime(clock);
}

/**
 * Stub the auth upstream with a configurable failure on /auth/refresh.
 *
 * `refreshStatus` of 429 / 502 / 'throw' are the TRANSIENT cases: the token was
 * never presented (the request did not reach the token endpoint), so it is still
 * valid and the session must survive.
 */
function stubAuth(options: {
  refreshStatus?: number | 'throw';
  expiresIn?: number;
  refreshExpiresIn?: number;
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

      if (options.refreshStatus === 'throw') {
        throw new TypeError('fetch failed: socket hang up');
      }
      if (typeof options.refreshStatus === 'number') {
        return new Response('{}', { status: options.refreshStatus });
      }
      return tokenResponse(`${state.refreshCalls + 1}`);
    }

    if (url.includes('/auth/logout')) return new Response(null, { status: 204 });
    return new Response('{}', { status: 404 });
  });

  return state;
}

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
  __setSessionStoreForTests(new MemorySessionStore());
  clock = 1_000_000;
  vi.useFakeTimers();
  vi.setSystemTime(clock);
});

afterEach(async () => {
  await getSessionStore().close();
  __setSessionStoreForTests(undefined);
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/**
 * HIGH 3: a transient upstream failure must NOT destroy the session.
 *
 * AuthStrict rate-limits /auth/refresh, so a 429 is the EXPECTED outcome during
 * a burst — and the previous code deleted the session on it, signing out exactly
 * the users a traffic spike should not affect.
 */
describe('transient upstream failures preserve the session', () => {
  it.each([
    ['429 rate limited', 429],
    ['502 bad gateway', 502],
    ['503 unavailable', 503],
  ])('survives a %s on refresh', async (_label, status) => {
    stubAuth({ refreshStatus: status });
    const session = await authenticatedSession();

    advance(299_000); // inside the refresh window

    expect(await getSession()).toBeDefined();

    // And the record is still there for the next request.
    expect(await getSessionStore().get(session.id)).not.toBeNull();
  });

  it('survives a thrown socket error on refresh', async () => {
    stubAuth({ refreshStatus: 'throw' });
    const session = await authenticatedSession();

    advance(299_000);

    expect(await getSession()).toBeDefined();
    expect(await getSessionStore().get(session.id)).not.toBeNull();
  });

  it('recovers on the next request once the upstream recovers', async () => {
    // First refresh fails transiently, the second succeeds. The session must
    // still be there to be recovered — otherwise "retry" is impossible.
    let failNext = true;
    const state = { presented: 0 };

    vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
      const url = String(input);
      const token = (suffix: string) =>
        new Response(
          JSON.stringify({
            access_token: `access-${suffix}`,
            refresh_token: `refresh-${suffix}`,
            expires_in: 300,
            refresh_expires_in: 1800,
            token_type: 'Bearer',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );

      if (url.includes('/auth/token')) return token('1');
      if (url.includes('/auth/refresh')) {
        state.presented += 1;
        if (failNext) {
          failNext = false;
          return new Response('{}', { status: 429 });
        }
        return token(`r${state.presented}`);
      }
      if (url.includes('/auth/logout')) return new Response(null, { status: 204 });
      return new Response('{}', { status: 404 });
    });

    await authenticatedSession();

    advance(299_000);
    await getSession(); // transient failure

    advance(1_000);
    const recovered = await getSession();

    expect(recovered).toBeDefined();
    expect(recovered?.tokens.current()?.refreshToken).not.toBe('refresh-1');
  });

  it('DESTROYS the session on a terminal 401 — the contrast case', async () => {
    // The distinction that makes the fix meaningful: 401 IS terminal.
    stubAuth({ refreshStatus: 401 });
    const session = await authenticatedSession();

    advance(299_000);

    expect(await getSession()).toBeUndefined();
    expect(await getSessionStore().get(session.id)).toBeNull();
  });
});

/**
 * HIGH 4: a lock loser must not be handed a session whose access token is
 * already inside the refresh window.
 *
 * The loser re-reads while the winner has not yet written, so it holds the OLD
 * pair. Returning that directly produced a 401 from the API for a user who is
 * genuinely authenticated.
 */
describe('a lock loser does not receive an expired access token', () => {
  // This case runs on the REAL clock. waitForRotation polls with setTimeout, and
  // under fake timers neither its poll loop nor any timeout guard advances unless
  // the test drives them by hand — which is what made three earlier versions of
  // this test hang for the full 5s and then time out.
  beforeEach(() => {
    vi.useRealTimers();
  });

  it('waits for the winner instead of returning the stale pair', async () => {
    stubAuth();
    const session = await authenticatedSession();
    // Real expiry maths: setSystemTime is gone, so shift the stored expiry by
    // editing the record directly rather than moving a fake clock.
    const store0 = getSessionStore();
    const seed = await store0.get(session.id);
    await store0.put({
      ...seed!,
      tokens: {
        ...seed!.tokens,
        expiresAt: Date.now() - 1, // already inside the refresh window
      },
    });

    const store = getSessionStore();
    let winnerGate: () => void = () => {};
    const winnerMayWrite = new Promise<void>((resolve) => {
      winnerGate = resolve;
    });

    let call = 0;
    const realLock = store.withRefreshLock.bind(store);
    vi.spyOn(store, 'withRefreshLock').mockImplementation(async (id, fn) => {
      call += 1;
      if (call === 1) {
        return realLock(id, async () => {
          await winnerMayWrite;
          return fn();
        });
      }
      return { acquired: false, value: null };
    });

    const winner = getSession();
    // Give the winner a tick to enter the lock before the loser arrives.
    await new Promise((r) => setTimeout(r, 20));

    const loser = getSession();
    const both = Promise.all([winner, loser]);

    // Let the loser start polling, then let the winner write.
    await new Promise((r) => setTimeout(r, 20));
    winnerGate();

    // Typed explicitly: Promise.race over a heterogeneous tuple widens to unknown,
    // which is not iterable — tsc rejects the destructure.
    const settled = (await Promise.race([
      both,
      new Promise<null>((resolve) => setTimeout(() => resolve(null), 3_000)),
    ])) as [unknown, unknown] | null;

    expect(settled, 'both callers settled before the timeout').not.toBeNull();

    const [winnerResult, loserResult] = settled as [
      { tokens: { current(): { refreshToken: string } | null } } | undefined,
      { tokens: { current(): { refreshToken: string } | null } } | undefined,
    ];

    expect(winnerResult, 'winner settled').toBeDefined();
    expect(loserResult, 'loser settled').toBeDefined();

    // The loser must receive the WINNER's rotated pair, not the stale one.
    const loserToken = loserResult?.tokens.current()?.refreshToken;
    const storedToken = (await store.get(session.id))?.tokens.refreshToken;

    expect(loserToken).toBe(storedToken);
    expect(loserToken).not.toBe('refresh-1');
  });
});

/**
 * LOW 7: a stored record missing presentedRefreshTokens must degrade to
 * "absent", matching the posture already taken for corrupt JSON.
 */
describe('a record with no presentedRefreshTokens array is treated as absent', () => {
  it('does not throw a TypeError on read', async () => {
    stubAuth();
    const session = await authenticatedSession();

    const store = getSessionStore();
    const stored = await store.get(session.id);

    // Write back a record with the field absent, as an older version or a
    // partial write would leave it.
    const broken = { ...stored! } as Record<string, unknown>;
    delete broken.presentedRefreshTokens;
    await store.put(broken as never);

    advance(299_000);

    // Must not throw. Either it resolves or it reports no usable session.
    await expect(getSession()).resolves.not.toThrow();
  });
});