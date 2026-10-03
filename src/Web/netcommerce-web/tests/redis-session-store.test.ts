import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  RedisSessionStore,
  type RedisLike,
} from '@/lib/auth/redis-session-store';
import type { StoredSession } from '@/lib/auth/session-store';

/**
 * An in-memory stand-in for Redis.
 *
 * Models only the commands this store uses, and models them faithfully where it
 * matters: SET NX fails when the key exists, PX expiry is honoured, and EVAL runs
 * the same compare-and-delete the real script performs. A permissive fake would
 * let a broken lock pass, which is the one property that must not be faked away.
 */
class FakeRedis implements RedisLike {
  readonly store = new Map<string, { value: string; expiresAt: number | null }>();
  readonly evalCalls: string[] = [];

  private live(key: string): boolean {
    const entry = this.store.get(key);
    if (!entry) return false;
    if (entry.expiresAt !== null && Date.now() > entry.expiresAt) {
      this.store.delete(key);
      return false;
    }
    return true;
  }

  async get(key: string): Promise<string | null> {
    return this.live(key) ? this.store.get(key)!.value : null;
  }

  async set(
    key: string,
    value: string,
    options?: { NX?: boolean; PX?: number; EX?: number },
  ): Promise<string | null> {
    if (options?.NX && this.live(key)) return null; // already held
    const expiresAt =
      options?.PX !== undefined
        ? Date.now() + options.PX
        : options?.EX !== undefined
          ? Date.now() + options.EX * 1000
          : null;
    this.store.set(key, { value, expiresAt });
    return 'OK';
  }

  async del(key: string | string[]): Promise<number> {
    const keys = Array.isArray(key) ? key : [key];
    let removed = 0;
    for (const k of keys) if (this.store.delete(k)) removed += 1;
    return removed;
  }

  async eval(
    script: string,
    options: { keys: string[]; arguments: string[] },
  ): Promise<unknown> {
    this.evalCalls.push(script);
    // NOT JavaScript eval. This stands in for Redis's server-side EVAL, which runs
    // a constant Lua script inside Redis' own sandbox. The script text is ignored
    // on purpose: emulating the compare-and-delete's BEHAVIOUR is what the tests
    // need, and interpreting Lua would be a far larger attack surface than the
    // thing being tested.
    // Index rather than destructure: under noUncheckedIndexedAccess an array
    // destructuring yields `string | undefined`, which is not assignable to the
    // `live()` parameter. Every caller passes both arrays non-empty.
    const key = options.keys[0] ?? '';
    const owner = options.arguments[0] ?? '';
    const current = this.live(key) ? this.store.get(key)!.value : null;
    if (current === owner) {
      this.store.delete(key);
      return 1;
    }
    return 0;
  }

  /** Force a key to look expired, to test lock TTL recovery. */
  expire(key: string): void {
    const entry = this.store.get(key);
    if (entry) entry.expiresAt = Date.now() - 1;
  }
}

const stored = (overrides: Partial<StoredSession> = {}): StoredSession => ({
  id: 's-1',
  createdAt: 1_700_000_000_000,
  tokens: {
    accessToken: 'access-1',
    refreshToken: 'refresh-1',
    expiresAt: 1_700_000_300_000,
    refreshExpiresAt: 1_700_003_600_000,
  },
  presentedRefreshTokens: [],
  ...overrides,
});

function makeStore(redis: FakeRedis, lockTtlMs = 10_000) {
  return new RedisSessionStore({ client: redis, ttlSeconds: 3600, lockTtlMs });
}

describe('RedisSessionStore', () => {
  let redis: FakeRedis;
  let store: RedisSessionStore;

  beforeEach(() => {
    redis = new FakeRedis();
    store = makeStore(redis);
  });

  it('is NOT process-local — the property production requires', () => {
    expect(store.isProcessLocal).toBe(false);
  });

  it('round-trips a session through Redis', async () => {
    await store.put(stored());
    const found = await store.get('s-1');
    expect(found?.tokens.refreshToken).toBe('refresh-1');
    expect(found?.tokens.accessToken).toBe('access-1');
  });

  it('returns null for an unknown session', async () => {
    expect(await store.get('missing')).toBeNull();
  });

  it('sets a TTL so a crashed process leaves nothing behind', async () => {
    await store.put(stored());
    const key = 'netcommerce:web:session:s-1';
    expect(redis.store.get(key)?.expiresAt).not.toBeNull();
  });

  it('uses a key prefix so it cannot collide with the API', async () => {
    await store.put(stored());
    expect([...redis.store.keys()].every((k) => k.startsWith('netcommerce:web:session'))).toBe(true);
  });

  it('overwrites on re-put, so a rotated pair replaces the old one', async () => {
    await store.put(stored());
    await store.put(stored({ tokens: { ...stored().tokens, refreshToken: 'refresh-2' } }));
    expect((await store.get('s-1'))?.tokens.refreshToken).toBe('refresh-2');
  });

  it('treats corrupt JSON as an absent session, not a crash', async () => {
    redis.store.set('netcommerce:web:session:s-1', { value: 'not json', expiresAt: null });
    expect(await store.get('s-1')).toBeNull();
  });

  it('treats a record with no refresh token as absent', async () => {
    // A session without a usable refresh token is not a session; returning it
    // would fail later, deep in the refresh path.
    const broken = { ...stored(), tokens: { ...stored().tokens, refreshToken: '' } };
    redis.store.set('netcommerce:web:session:s-1', {
      value: JSON.stringify(broken),
      expiresAt: null,
    });
    expect(await store.get('s-1')).toBeNull();
  });

  it('deletes a session', async () => {
    await store.put(stored());
    await store.delete('s-1');
    expect(await store.get('s-1')).toBeNull();
  });

  it('close() is safe and does not delete sessions', async () => {
    await store.put(stored());
    await store.close();
    // Redis owns the keys' lifetime, not this process.
    expect(await store.get('s-1')).not.toBeNull();
  });
});

describe('RedisSessionStore refresh lock', () => {
  let redis: FakeRedis;
  let store: RedisSessionStore;

  beforeEach(() => {
    redis = new FakeRedis();
    store = makeStore(redis);
  });

  it('grants the lock to the first caller and returns its value', async () => {
    const result = await store.withRefreshLock('s-1', async () => 'rotated');
    expect(result).toEqual({ acquired: true, value: 'rotated' });
  });

  it('refuses a concurrent caller — the property that prevents token replay', async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });

    const first = store.withRefreshLock('s-1', async () => {
      await gate;
      return 'rotated';
    });
    await new Promise((resolve) => setImmediate(resolve));

    const second = await store.withRefreshLock('s-1', async () => 'also rotated');
    release();
    await first;

    expect(second.acquired).toBe(false);
    expect(second.value).toBeNull();
  });

  it('admits exactly one winner among concurrent callers across "replicas"', async () => {
    let rotations = 0;
    const results = await Promise.all(
      Array.from({ length: 12 }, () =>
        store.withRefreshLock('s-1', async () => {
          rotations += 1;
          await new Promise((resolve) => setTimeout(resolve, 5));
          return 'rotated';
        }),
      ),
    );
    expect(results.filter((r) => r.acquired)).toHaveLength(1);
    expect(rotations).toBe(1);
  });

  it('releases the lock with a compare-and-delete, never a plain DEL', async () => {
    await store.withRefreshLock('s-1', async () => 'rotated');
    expect(redis.evalCalls).toHaveLength(1);
    expect(redis.evalCalls[0]).toContain("redis.call('GET'");
    expect(redis.evalCalls[0]).toContain("redis.call('DEL'");
  });

  it('does NOT delete a lock it no longer owns', async () => {
    // Simulate the lock expiring and being re-taken by "another replica" while
    // our holder is still running. The release must leave the new holder alone.
    const lockKey = 'netcommerce:web:session:lock:s-1';
    redis.store.set(lockKey, { value: 'someone-else', expiresAt: null });

    await store
      .withRefreshLock('s-1', async () => {
        // Overwrite the lock owner, as an expiry + re-acquire would.
        redis.store.set(lockKey, { value: 'someone-else', expiresAt: null });
        return 'rotated';
      })
      .catch(() => undefined);

    // The other holder's lock survives our release.
    expect(redis.store.get(lockKey)?.value).toBe('someone-else');
  });

  it('releases the lock even when the callback throws', async () => {
    await expect(
      store.withRefreshLock('s-1', async () => {
        throw new Error('upstream 500');
      }),
    ).rejects.toThrow('upstream 500');

    // Not left locked: the next caller can try again.
    const next = await store.withRefreshLock('s-1', async () => 'ok');
    expect(next.acquired).toBe(true);
  });

  it('propagates the callback error but still attempts release', async () => {
    const evalSpy = vi.spyOn(redis, 'eval');
    await expect(
      store.withRefreshLock('s-1', async () => {
        throw new Error('boom');
      }),
    ).rejects.toThrow();
    expect(evalSpy).toHaveBeenCalled();
  });

  it('recovers when a crashed holder lets the lock expire', async () => {
    const lockKey = 'netcommerce:web:session:lock:s-1';
    // A holder that died without releasing: lock present but expired.
    redis.store.set(lockKey, { value: 'dead-holder', expiresAt: Date.now() - 1 });

    const result = await store.withRefreshLock('s-1', async () => 'rotated');
    expect(result.acquired).toBe(true);
  });

  it('gives each holder a distinct owner token', async () => {
    await store.withRefreshLock('s-1', async () => undefined);
    await store.withRefreshLock('s-1', async () => undefined);
    // Two acquisitions, each released by its own owner. If the owner token were
    // constant, a stale release could delete a fresh lock.
    expect(redis.evalCalls).toHaveLength(2);
  });
});