import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  RedisSessionStore,
  type RedisLike,
} from '@/lib/auth/redis-session-store';
import { MemorySessionStore } from '@/lib/auth/session-store';

/**
 * Regression tests for the audit's BLOCKER 2: the refresh lock's TTL was never
 * renewed.
 *
 * The lock was `SET NX PX 10000` and then nothing. If a token round trip took
 * longer than the TTL — a slow Keycloak, a GC pause, a saturated connection
 * pool — Redis expired the lock while the holder was still rotating. A second
 * replica then acquired the same lock, read the stored record BEFORE the holder
 * had written, saw the refresh token as unpresented, and rotated it a second
 * time.
 *
 * Because these Keycloak refresh tokens are single-use, the second presentation
 * revokes the ENTIRE session family. One slow rotation logs out that user on
 * every device, everywhere, with no error anywhere.
 *
 * The FakeRedis below honours PX expiry and NX, so an unrenewed lock genuinely
 * expires here. That is the property a permissive fake would hide.
 */

const ID = 'sess-1';

function storedSession(refreshToken = 'refresh-1'): {
  id: string;
  createdAt: number;
  tokens: {
    accessToken: string;
    refreshToken: string;
    expiresAt: number;
    refreshExpiresAt: number;
    issuedAt: number;
  };
  presentedRefreshTokens: string[];
} {
  return {
    id: ID,
    createdAt: Date.now(),
    tokens: {
      accessToken: 'access-1',
      refreshToken,
      expiresAt: Date.now() + 300_000,
      refreshExpiresAt: Date.now() + 1_800_000,
      issuedAt: Date.now(),
    },
    presentedRefreshTokens: [],
  };
}

class FakeRedis implements RedisLike {
  readonly store = new Map<string, { value: string; expiresAt: number | null }>();
  /** Every Lua script this fake was asked to run, in order. */
  readonly evalCalls: string[] = [];
  /** Fail the Nth PEXPIRE-style renewal, simulating a lost lease. */
  renewFailsFromCall = Number.POSITIVE_INFINITY;

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
    if (options?.NX && this.live(key)) return null;
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
    // Redis's server-side EVAL, not JavaScript eval: a constant Lua script run
    // inside Redis' own sandbox. The script TEXT is ignored deliberately —
    // emulating the behaviour (owner-checked release / renewal) is what these
    // tests need, and interpreting Lua would be a far larger surface than the
    // thing under test.
    const key = options.keys[0] ?? '';
    const owner = options.arguments[0] ?? '';

    // Distinguish renewal (PEXPIRE) from release (DEL) by the constant body.
    if (script.includes('PEXPIRE')) {
      if (this.evalCalls.filter((s) => s.includes('PEXPIRE')).length >= this.renewFailsFromCall) {
        return 0; // lease lost
      }
      if (!this.live(key) || this.store.get(key)!.value !== owner) return 0;
      const ttl = Number(options.arguments[1] ?? 0);
      this.store.set(key, { value: owner, expiresAt: Date.now() + ttl });
      return 1;
    }

    if (this.live(key) && this.store.get(key)!.value === owner) {
      this.store.delete(key);
      return 1;
    }
    return 0;
  }

  /** Count renewal attempts, ignoring the release script. */
  renewCount(): number {
    return this.evalCalls.filter((s) => s.includes('PEXPIRE')).length;
  }
}

describe('BLOCKER 2: the refresh lock lease is renewed while work is in flight', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(1_000_000);
  });

  it('keeps the lock held across a rotation longer than the TTL', async () => {
    const redis = new FakeRedis();
    // A 100ms TTL makes the expiry observable without a 10-second test.
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: 3600,
      lockTtlMs: 100,
    });

    const rotation = store.withRefreshLock(ID, async () => {
      // Simulate a token round trip far slower than the lease: 500ms.
      await new Promise((r) => setTimeout(r, 500));
      return 'rotated';
    });

    // Advance past the TTL several times. Without renewal the lock is long gone.
    for (let i = 0; i < 10; i += 1) {
      await vi.advanceTimersByTimeAsync(100);
    }

    const result = await rotation;
    expect(result).toEqual({ acquired: true, value: 'rotated' });

    // The lock was still ours throughout — renewal actually ran.
    expect(redis.renewCount()).toBeGreaterThan(0);
  });

  it('does NOT let a second replica in while a slow rotation runs', async () => {
    const redis = new FakeRedis();
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: 3600,
      lockTtlMs: 100,
    });

    let secondAcquired = false;
    const slow = store.withRefreshLock(ID, async () => {
      await new Promise((r) => setTimeout(r, 500));
      return 'first';
    });

    // Halfway through the slow rotation, a second replica tries the same lock.
    await vi.advanceTimersByTimeAsync(250);
    const second = await store.withRefreshLock(ID, async () => 'second');
    secondAcquired = second.acquired;

    await vi.advanceTimersByTimeAsync(400);

    expect((await slow).value).toBe('first');
    expect(secondAcquired).toBe(false);
  });
});

describe('a lost lease is detected before the result is committed', () => {
  // REAL timers for these cases.
  //
  // The work here awaits its own setTimeout to simulate a slow rotation, and the
  // guard then does a Redis round trip. Under fake timers the work's sleep never
  // fires unless the test drives it by hand — which is what made the first three
  // versions of this suite sit at exactly 5000ms and time out.
  beforeEach(() => {
    vi.useRealTimers();
  });

  it('reports the lease as lost when renewal is refused', async () => {
    const redis = new FakeRedis();
    // Every renewal from the first attempt onwards fails: the lease was stolen.
    redis.renewFailsFromCall = 1;
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: 3600,
      lockTtlMs: 100,
    });

    let stillHeldAtCommit: boolean | null = null;

    const result = await store.withRefreshLock(ID, async (guard) => {
      await new Promise((r) => setTimeout(r, 250));
      // The caller MUST be able to ask "is this still mine?" right before it
      // writes. Renewal having failed is the only warning we get.
      stillHeldAtCommit = await guard.isHeld();
      return 'rotated';
    });

    expect(result.acquired).toBe(true);
    expect(stillHeldAtCommit).toBe(false);
  });

  it('reports the lease as held while renewal is succeeding', async () => {
    const redis = new FakeRedis();
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: 3600,
      lockTtlMs: 100,
    });

    let stillHeldAtCommit: boolean | null = null;
    await store.withRefreshLock(ID, async (guard) => {
      await new Promise((r) => setTimeout(r, 250));
      stillHeldAtCommit = await guard.isHeld();
      return 'rotated';
    });

    expect(stillHeldAtCommit).toBe(true);
  });

  it('throws from assertHeld so a caller cannot forget to check', async () => {
    const redis = new FakeRedis();
    redis.renewFailsFromCall = 1;
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: 3600,
      lockTtlMs: 100,
    });

    await expect(
      store.withRefreshLock(ID, async (guard) => {
        await new Promise((r) => setTimeout(r, 250));
        await guard.assertHeld();
        return 'never reached';
      }),
    ).rejects.toThrow(/lock|lease/i);
  });

  it('releases the lock on the happy path so no stale lock is left behind', async () => {
    const redis = new FakeRedis();
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: 3600,
      lockTtlMs: 100,
    });

    await store.withRefreshLock(ID, async () => 'done');
    // Real timers in this block, so let a turn of the loop pass rather than
    // calling advanceTimersByTimeAsync (which is a no-op on a real clock).
    await new Promise((r) => setTimeout(r, 5));

    // Released, so a later caller can acquire immediately.
    const next = await store.withRefreshLock(ID, async () => 'again');
    expect(next).toEqual({ acquired: true, value: 'again' });
  });

  it('releases the lock even when the work throws', async () => {
    const redis = new FakeRedis();
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: 3600,
      lockTtlMs: 100,
    });

    await expect(
      store.withRefreshLock(ID, async () => {
        throw new Error('rotation exploded');
      }),
    ).rejects.toThrow('rotation exploded');

    const next = await store.withRefreshLock(ID, async () => 'recovered');
    expect(next.acquired).toBe(true);
  });
});

describe('the memory store honours the same guard contract', () => {
  // Real timers: the work awaits its own sleep, and the memory store's guard is
  // synchronous underneath, so a fake clock only adds a way to hang.
  beforeEach(() => {
    vi.useRealTimers();
  });

  it('reports the lock as held and does not throw from assertHeld', async () => {
    const store = new MemorySessionStore();

    const result = await store.withRefreshLock(ID, async (guard) => {
      await new Promise((r) => setTimeout(r, 50));
      expect(await guard.isHeld()).toBe(true);
      await expect(guard.assertHeld()).resolves.toBeUndefined();
      return 'rotated';
    });

    expect(result).toEqual({ acquired: true, value: 'rotated' });
  });

  it('still excludes a concurrent caller in the same process', async () => {
    const store = new MemorySessionStore();

    const first = store.withRefreshLock(ID, async () => {
      await new Promise((r) => setTimeout(r, 120));
      return 'first';
    });

    await new Promise((r) => setTimeout(r, 20));
    const second = await store.withRefreshLock(ID, async () => 'second');

    expect(second.acquired).toBe(false);
    expect((await first).value).toBe('first');
  });
});