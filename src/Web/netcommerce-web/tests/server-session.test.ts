import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * The session registry is the security-critical piece: it decides whether a
 * request is authenticated, and it holds live refresh tokens.
 *
 * These tests cover the lifecycle that a Map-based registry gets wrong by
 * default — unbounded growth and sessions that outlive their cookie.
 */

// `next/headers` is only available inside a Next request scope.
const cookieJar = new Map<string, string>();
vi.mock('next/headers', () => ({
  cookies: async () => ({
    get: (name: string) =>
      cookieJar.has(name) ? { name, value: cookieJar.get(name)! } : undefined,
    set: (name: string, value: string) => cookieJar.set(name, value),
    delete: (name: string) => cookieJar.delete(name),
  }),
}));

vi.mock('@/lib/config', () => ({
  readConfig: () => ({
    apiBaseUrl: 'http://api',
    keycloakBaseUrl: 'http://keycloak',
    keycloakRealm: 'netcommerce',
    keycloakClientId: 'netcommerce-web',
    callbackPath: '/callback',
  }),
  publicOrigin: (u: string) => new URL(u).origin,
}));

import {
  __resetRegistryForTests,
  __registrySize,
  getSession,
  getSessionById,
  registerSession,
  SESSION_ABSOLUTE_TTL_MS,
} from '@/lib/auth/server-session';
import { SESSION_COOKIE } from '@/lib/auth/session-cookie';

let clock = 1_000_000;

/** Advance the mocked system clock (not just a local variable). */
function advance(ms: number) {
  clock += ms;
  vi.setSystemTime(clock);
}

beforeEach(() => {
  cookieJar.clear();
  __resetRegistryForTests();
  clock = 1_000_000;
  vi.useFakeTimers();
  vi.setSystemTime(clock);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

/** Register a session and make it look logged-in. */
function authenticatedSession(id?: string) {
  const session = registerSession();
  // Simulate a successful exchange without touching the network. adoptForTest is
  // an OPTIONAL test seam on TokenStore precisely so production code cannot call
  // it, which means the test has to assert it exists before using it.
  const adopt = session.tokens.adoptForTest;
  if (!adopt) throw new Error('adoptForTest test seam is missing');
  adopt({
    access_token: 'access-1',
    refresh_token: 'refresh-1',
    expires_in: 300,
    refresh_expires_in: 1800,
    token_type: 'Bearer',
  });
  cookieJar.set(SESSION_COOKIE, id ?? session.id);
  return session;
}

describe('session registry', () => {
  it('resolves a registered session by id', () => {
    const session = authenticatedSession();
    expect(getSessionById(session.id)?.id).toBe(session.id);
  });

  it('returns undefined for an unknown id', () => {
    expect(getSessionById('not-a-real-session')).toBeUndefined();
    expect(getSessionById(undefined)).toBeUndefined();
  });

  it('generates a distinct id per session', () => {
    const ids = new Set(Array.from({ length: 25 }, () => registerSession().id));
    expect(ids.size).toBe(25);
  });

  it('generates an id long enough to resist guessing', () => {
    expect(registerSession().id.length).toBeGreaterThanOrEqual(64);
  });

  it('resolves the session for a request carrying the cookie', async () => {
    const session = authenticatedSession();
    const found = await getSession();
    expect(found?.id).toBe(session.id);
  });

  it('returns undefined when no session cookie is present', async () => {
    expect(await getSession()).toBeUndefined();
  });

  it('expires and evicts a session past the absolute TTL', async () => {
    const session = authenticatedSession();
    // Keep the refresh token's own lifetime long enough that the ABSOLUTE TTL is
    // what ends this session; otherwise the refreshExpiresAt check fires first
    // and the test would pass for the wrong reason.
    session.tokens.adoptForTest?.({
      access_token: 'access-1',
      refresh_token: 'refresh-1',
      expires_in: 300,
      refresh_expires_in: SESSION_ABSOLUTE_TTL_MS * 4,
      token_type: 'Bearer',
    });
    const keepAlive = vi.fn().mockImplementation(async () => session.tokens.current());
    session.tokens.refresh = keepAlive;

    // Just inside the TTL: still alive, and it was refreshed to stay so.
    advance(SESSION_ABSOLUTE_TTL_MS - 1_000);
    expect(await getSession()).toBeDefined();
    expect(keepAlive).toHaveBeenCalled();

    // Past it: gone from memory, not merely ignored, and NOT refreshable.
    keepAlive.mockClear();
    advance(2_000);
    expect(await getSession()).toBeUndefined();
    expect(getSessionById(session.id)).toBeUndefined();
    expect(keepAlive).not.toHaveBeenCalled();
  });

  it('ends the session when the refresh token itself expires, even inside the TTL', async () => {
    const session = authenticatedSession();
    // Default fixture: refresh_expires_in is 1800s (30 min), far inside the 8h TTL.
    session.tokens.refresh = vi.fn().mockResolvedValue(null);

    advance(1_800_000); // exactly refresh_expires_in
    expect(await getSession()).toBeUndefined();
    expect(getSessionById(session.id)).toBeUndefined();
    // It must not even attempt a rotation with an expired token.
    expect(session.tokens.refresh).not.toHaveBeenCalled();
  });

  it('does not let refresh keep a session alive forever', async () => {
    const session = authenticatedSession();
    session.tokens.refresh = vi.fn().mockResolvedValue(null);

    // Keep asking; the absolute TTL must still cut it off.
    for (let i = 0; i < 5; i += 1) {
      advance(SESSION_ABSOLUTE_TTL_MS / 2);
      await getSession();
    }

    expect(await getSession()).toBeUndefined();
    expect(getSessionById(session.id)).toBeUndefined();
  });

  it('evicts a session whose refresh failed, so the next request is a clean login', async () => {
    const session = authenticatedSession();
    session.tokens.refresh = vi.fn().mockResolvedValue(null);
    session.tokens.needsRefresh = () => true;

    expect(await getSession()).toBeUndefined();
    expect(getSessionById(session.id)).toBeUndefined();
  });

  it('sweeps expired entries so the registry cannot grow without bound', () => {
    // Create many sessions, then age past the TTL.
    for (let i = 0; i < 50; i += 1) authenticatedSession();
    expect(__registrySize()).toBe(50);

    advance(SESSION_ABSOLUTE_TTL_MS + 1_000);
    // Any subsequent registry operation sweeps.
    getSessionById('anything');

    expect(__registrySize()).toBe(0);
  });
});
