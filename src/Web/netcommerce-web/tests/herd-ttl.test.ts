import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  RedisSessionStore,
  type RedisLike,
} from '@/lib/auth/redis-session-store';
import type { SessionStore, StoredSession } from '@/lib/auth/session-store';
import { waitForRotationForTest } from '@/lib/auth/server-session';

/**
 * Regression tests for two review findings.
 *
 * FINDING A — thundering herd. `waitForRotation` polls every 25ms for up to 5s
 * waiting for a winner to write a new token pair. But when the winner's refresh
 * FAILS TRANSIENTLY (AuthStrict returns 429 — the expected outcome of a login
 * burst), no new pair is ever written. The condition the loop watches for never
 * becomes true, so every loser polls for the full 5 seconds: with N concurrent
 * requests that is 40N Redis GETs/second, all callers held at maximum latency,
 * and on the next request cycle the identical race re-runs.
 *
 * The loop can tell it is wasting time. It knows the session id, and the lock is
 * gone once the winner finishes — polling a lock that no longer exists is
 * provably futile, because nothing else can write that pair.
 *
 * FINDING B — sliding expiry. `put()` wrote `EX ttlSeconds` with a CONSTANT, so
 * every rotation reset the TTL to a full 8 hours from now. An active session
 * never expired, turning a documented absolute cap into a sliding window and
 * leaving ghost records in Redis. The in-memory `isExpired()` check still refused
 * the session on read, so a user never noticed — but the record lingered, and
 * the store's own comment claimed the opposite of what the code did.
 */

const ID = 'sess-1';
const TTL_MS = 8 * 60 * 60 * 1000;

function session(createdAt: number, refreshToken = 'refresh-1'): StoredSession {
  return {
    id: ID,
    createdAt,
    tokens: {
      accessToken: 'access-1',
      refreshToken,
      expiresAt: createdAt + 300_000,
      refreshExpiresAt: createdAt + 1_800_000,
    },
    presentedRefreshTokens: [],
  };
}

/**
 * A Redis stand-in that COUNTS reads.
 *
 * Read counting is the whole point: the defect is invisible in the return value
 * (the caller gets its record either way) and only shows up as request volume.
 */
class CountingRedis implements RedisLike {
  readonly store = new Map<string, { value: string; expiresAt: number | null }>();
  readonly sets: { key: string; ex?: number; px?: number; nx?: boolean }[] = [];
  getCalls = 0;

  private live(key: string): boolean {
    const e = this.store.get(key);
    if (!e) return false;
    if (e.expiresAt !== null && Date.now() > e.expiresAt) {
      this.store.delete(key);
      return false;
    }
    return true;
  }

  async get(key: string): Promise<string | null> {
    this.getCalls += 1;
    return this.live(key) ? this.store.get(key)!.value : null;
  }

  async set(
    key: string,
    value: string,
    options?: { NX?: boolean; PX?: number; EX?: number },
  ): Promise<string | null> {
    this.sets.push({ key, ex: options?.EX, px: options?.PX, nx: options?.NX });
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
    let n = 0;
    for (const k of keys) if (this.store.delete(k)) n += 1;
    return n;
  }

  async eval(
    _script: string,
    options: { keys: string[]; arguments: string[] },
  ): Promise<unknown> {
    // Redis's SERVER-SIDE EVAL — a constant Lua script run inside Redis' own
    // sandbox — not JavaScript eval. This is a method declaration implementing the
    // RedisLike interface, and it takes no user input: the only argument is the
    // owner token this process generated.
    const key = options.keys[0] ?? '';
    const owner = options.arguments[0] ?? '';
    if (this.live(key) && this.store.get(key)!.value === owner) {
      this.store.delete(key);
      return 1;
    }
    return 0;
  }
}

/* ------------------------------------------------------------------ *
 * FINDING A — waiters must not poll a lock that no longer exists
 * ------------------------------------------------------------------ */

describe('a waiter stops polling once the winner has finished', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(1_000_000);
  });

  /**
   * The decisive scenario: the winner's refresh failed transiently, so it
   * released the lock WITHOUT writing a new pair. A waiter can never be
   * satisfied by a token change, and must break out early.
   */
  it('returns promptly instead of polling for the full timeout', async () => {
    const redis = new CountingRedis();
    const store: SessionStore = new RedisSessionStore({
      client: redis,
      ttlSeconds: TTL_MS / 1000,
      lockTtlMs: 10_000,
    });

    await store.put(session(Date.now()));
    const original = (await store.get(ID))!;

    // The winner takes the lock and fails transiently — it never writes.
    const winner = await store.withRefreshLock(ID, async () => {
      // transient 429: session retained, nothing written
      return null;
    });
    expect(winner.acquired).toBe(true);
    expect((await store.get(ID))!.tokens.refreshToken).toBe(
      original.tokens.refreshToken,
    );

    redis.getCalls = 0;

    // A waiter arrives. With the fix it breaks out within a couple of polls; the
    // unbounded case would spin 5000/25 = 200 times.
    //
    // All arguments are passed explicitly rather than relying on defaults: the
    // defaults for `sleep` and `isLockHeld` exist for PRODUCTION wiring, and a
    // test that inherited them would not exercise the lock check at all.
    const waiter = waitForRotationForTest(
      store,
      ID,
      original,
      {
        now: () => Date.now(),
        waitMs: 5_000,
        sleep: () => new Promise((r) => setTimeout(r, 25)),
        isLockHeld: () => store.isRefreshLockHeld!(ID),
      },
    );
    await vi.advanceTimersByTimeAsync(5_500);
    const result = await waiter;

    expect(result).not.toBeNull();
    expect(redis.getCalls).toBeLessThan(10);
  });

  it('still waits when a rotation really is in flight', async () => {
    // The contrast case. If the lock is genuinely held, a waiter must KEEP
    // polling — the winner is mid-rotation and a new pair will land shortly.
    // Without this, "break early" would destroy the behaviour the loop exists for.
    const redis = new CountingRedis();
    const store: SessionStore = new RedisSessionStore({
      client: redis,
      ttlSeconds: TTL_MS / 1000,
      lockTtlMs: 10_000,
    });

    await store.put(session(Date.now()));
    const original = (await store.get(ID))!;

    // Hold the lock, and write a new pair partway through.
    const winner = store.withRefreshLock(ID, async () => {
      await new Promise((r) => setTimeout(r, 200));
      await store.put(session(original.createdAt, 'refresh-2'));
      return 'rotated';
    });
    await vi.advanceTimersByTimeAsync(0);

    redis.getCalls = 0;
    const waiter = waitForRotationForTest(
      store,
      ID,
      original,
      {
        now: () => Date.now(),
        waitMs: 5_000,
        sleep: () => new Promise((r) => setTimeout(r, 25)),
        isLockHeld: () => store.isRefreshLockHeld!(ID),
      },
    );
    await vi.advanceTimersByTimeAsync(600);

    const result = await waiter;
    await winner;

    // It saw the NEW pair, not the stale one.
    expect(result?.tokens.refreshToken).toBe('refresh-2');
    expect(redis.getCalls).toBeGreaterThan(1);
  });

  it('returns null when the session disappeared entirely', async () => {
    const redis = new CountingRedis();
    const store: SessionStore = new RedisSessionStore({
      client: redis,
      ttlSeconds: TTL_MS / 1000,
      lockTtlMs: 10_000,
    });
    // Nothing stored: the winner deleted the session.
    const waiter = waitForRotationForTest(
      store,
      ID,
      session(1_000_000),
      {
        now: () => Date.now(),
        waitMs: 5_000,
        sleep: () => new Promise((r) => setTimeout(r, 25)),
        isLockHeld: () => store.isRefreshLockHeld!(ID),
      },
    );
    await vi.advanceTimersByTimeAsync(200);
    expect(await waiter).toBeNull();
  });
});

/* ------------------------------------------------------------------ *
 * FINDING B — put() must not extend the absolute TTL
 * ------------------------------------------------------------------ */

describe('put() writes the REMAINING lifetime, not a fresh full TTL', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(1_000_000);
  });

  it('shrinks the EX value as the session ages', async () => {
    const redis = new CountingRedis();
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: Math.ceil(TTL_MS / 1000),
      lockTtlMs: 10_000,
    });

    const createdAt = Date.now();
    await store.put(session(createdAt));
    const first = redis.sets.at(-1)!.ex!;
    expect(first).toBe(Math.ceil(TTL_MS / 1000));

    // Seven hours later a rotation rewrites the record. The TTL must now be ~1h,
    // NOT another full 8h — otherwise the cap never bites.
    vi.setSystemTime(createdAt + 7 * 60 * 60 * 1000);
    await store.put(session(createdAt, 'refresh-2'));
    const second = redis.sets.at(-1)!.ex!;

    expect(second).toBeLessThan(first);
    expect(second).toBe(Math.ceil((TTL_MS - 7 * 60 * 60 * 1000) / 1000));
  });

  it('does not resurrect an already-expired session with a long TTL', async () => {
    const redis = new CountingRedis();
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: Math.ceil(TTL_MS / 1000),
      lockTtlMs: 10_000,
    });

    const createdAt = Date.now();
    // Nine hours in: past the absolute cap.
    vi.setSystemTime(createdAt + 9 * 60 * 60 * 1000);
    await store.put(session(createdAt, 'refresh-2'));

    const ex = redis.sets.at(-1)!.ex!;
    // Clamped to the minimum, so Redis clears it imminently rather than keeping
    // a dead session for another 8 hours.
    expect(ex).toBeGreaterThanOrEqual(1);
    expect(ex).toBeLessThanOrEqual(2);
  });

  it('the remaining lifetime never exceeds the absolute cap', async () => {
    const redis = new CountingRedis();
    const store = new RedisSessionStore({
      client: redis,
      ttlSeconds: Math.ceil(TTL_MS / 1000),
      lockTtlMs: 10_000,
    });
    // A record written "before" its own creation time — clock skew or a restored
    // backup. The TTL must still be bounded by the cap, not inflated.
    const createdAt = Date.now() + 60 * 60 * 1000;
    await store.put(session(createdAt));
    expect(redis.sets.at(-1)!.ex!).toBeLessThanOrEqual(Math.ceil(TTL_MS / 1000));
  });
});