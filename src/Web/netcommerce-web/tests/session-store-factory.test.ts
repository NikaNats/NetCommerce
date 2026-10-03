import { describe, expect, it, vi } from 'vitest';

import {
  assertProductionSessionStore,
  createSessionStore,
  resolveSessionStoreKind,
  SessionStoreConfigError,
} from '@/lib/auth/session-store-factory';
import { MemorySessionStore, type SessionStore } from '@/lib/auth/session-store';
import type { RedisLike } from '@/lib/auth/redis-session-store';
import { SESSION_ABSOLUTE_TTL_MS } from '@/lib/auth/session-store-ttl';

/** Minimal Redis stand-in; the factory only needs the type, not behaviour. */
const fakeRedis = {} as RedisLike;

describe('resolveSessionStoreKind', () => {
  it('defaults to memory when nothing is configured', () => {
    expect(resolveSessionStoreKind({})).toBe('memory');
  });

  it('infers redis when REDIS_URL is present, so no extra flag is needed', () => {
    expect(resolveSessionStoreKind({ REDIS_URL: 'redis://cache:6379' })).toBe('redis');
  });

  it('honours an explicit SESSION_STORE=memory even with a REDIS_URL', () => {
    // A deliberate single-replica opt-out must win, so a dev with Redis available
    // can still test the in-memory path.
    expect(
      resolveSessionStoreKind({ SESSION_STORE: 'memory', REDIS_URL: 'redis://cache:6379' }),
    ).toBe('memory');
  });

  it('honours SESSION_STORE=redis', () => {
    expect(resolveSessionStoreKind({ SESSION_STORE: 'redis' })).toBe('redis');
  });

  it('is case- and whitespace-insensitive', () => {
    expect(resolveSessionStoreKind({ SESSION_STORE: '  REDIS ' })).toBe('redis');
  });

  it('ignores an empty REDIS_URL', () => {
    expect(resolveSessionStoreKind({ REDIS_URL: '   ' })).toBe('memory');
  });
});

/**
 * The guard is the whole point of this change: a process-local store in
 * production signs users out at random and never throws.
 */
describe('assertProductionSessionStore', () => {
  it('REFUSES a process-local store in production', () => {
    expect(() =>
      assertProductionSessionStore('memory', { NODE_ENV: 'production' }),
    ).toThrow(SessionStoreConfigError);
  });

  it('explains the actual failure, not just the rule', () => {
    // A message naming "randomly signed out" is actionable at 3am; "invalid
    // configuration" is not.
    expect(() =>
      assertProductionSessionStore('memory', { NODE_ENV: 'production' }),
    ).toThrow(/signed out at random/i);
  });

  it('allows a process-local store OUTSIDE production', () => {
    expect(() =>
      assertProductionSessionStore('memory', { NODE_ENV: 'development' }),
    ).not.toThrow();
  });

  it('allows it in test too, which is what keeps the suite runnable', () => {
    expect(() => assertProductionSessionStore('memory', { NODE_ENV: 'test' })).not.toThrow();
  });

  it('allows redis in production', () => {
    expect(() =>
      assertProductionSessionStore('redis', {
        NODE_ENV: 'production',
        REDIS_URL: 'redis://cache:6379',
      }),
    ).not.toThrow();
  });

  it('refuses SESSION_STORE=redis with no REDIS_URL', () => {
    // Otherwise the store would be constructed and fail on the first login.
    expect(() =>
      assertProductionSessionStore('redis', { NODE_ENV: 'production' }),
    ).toThrow(/REDIS_URL/);
  });
});

describe('createSessionStore', () => {
  it('builds a process-local store in development', () => {
    const store = createSessionStore({ env: { NODE_ENV: 'development' } });
    expect(store).toBeInstanceOf(MemorySessionStore);
    expect(store.isProcessLocal).toBe(true);
  });

  it('builds a shared store when Redis is configured', () => {
    const store = createSessionStore({
      env: { NODE_ENV: 'production', REDIS_URL: 'redis://cache:6379' },
      redisClient: fakeRedis,
    });
    expect(store.isProcessLocal).toBe(false);
  });

  it('refuses to build a process-local store in production', () => {
    expect(() =>
      createSessionStore({ env: { NODE_ENV: 'production' } }),
    ).toThrow(SessionStoreConfigError);
  });

  it('cannot be talked into a broken production store by SESSION_STORE=memory', () => {
    // The escape hatch is development-only BY DESIGN. If this ever stops
    // throwing, the silent-logout bug is shippable again.
    expect(() =>
      createSessionStore({
        env: { NODE_ENV: 'production', SESSION_STORE: 'memory' },
      }),
    ).toThrow(SessionStoreConfigError);
  });

  it('fails at startup rather than lazily when Redis is requested without a client', () => {
    // A lazy connect would surface on a customer's login instead of on deploy.
    //
    // The MESSAGE changed when the audit found this unreachable in production.
    // It used to read "no Redis client was provided" and was only ever triggered
    // by an injected client in tests — nothing in the running app constructed
    // one, so the redis path could not be reached at all. It now fails with a
    // named SessionStoreConfigError explaining that the client must be
    // established at startup by instrumentation.ts.
    expect(() =>
      createSessionStore({
        env: { NODE_ENV: 'production', REDIS_URL: 'redis://cache:6379' },
      }),
    ).toThrow(SessionStoreConfigError);

    // And it must stay a NAMED error, not a generic throw, so the cause is
    // legible in a deploy log.
    expect(() =>
      createSessionStore({
        env: { NODE_ENV: 'production', REDIS_URL: 'redis://cache:6379' },
      }),
    ).toThrow(/no client is connected|REDIS_URL/i);
  });

  it('derives the Redis TTL from the absolute session lifetime', async () => {
    const set = vi.fn().mockResolvedValue('OK');
    const client = { set, get: vi.fn(), del: vi.fn(), eval: vi.fn() } as unknown as RedisLike;
    const store = createSessionStore({
      env: { NODE_ENV: 'production', REDIS_URL: 'redis://cache:6379' },
      redisClient: client,
    });

    await store.put({
      id: 's-1',
      createdAt: Date.now(),
      tokens: {
        accessToken: 'a',
        refreshToken: 'r',
        expiresAt: Date.now() + 1000,
        refreshExpiresAt: Date.now() + 1000,
      },
      presentedRefreshTokens: [],
    });

    const expectedSeconds = Math.ceil(SESSION_ABSOLUTE_TTL_MS / 1000);
    expect(set).toHaveBeenCalledWith(
      expect.any(String),
      expect.any(String),
      expect.objectContaining({ EX: expectedSeconds }),
    );
  });

  it('satisfies the SessionStore contract', () => {
    const store: SessionStore = createSessionStore({ env: { NODE_ENV: 'development' } });
    expect(typeof store.withRefreshLock).toBe('function');
    expect(typeof store.get).toBe('function');
    expect(typeof store.put).toBe('function');
    expect(typeof store.delete).toBe('function');
  });
});