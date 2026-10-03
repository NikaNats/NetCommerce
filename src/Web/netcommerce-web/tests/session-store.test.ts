import { describe, expect, it } from 'vitest';

import {
  MemorySessionStore,
  type SessionStore,
  type StoredSession,
} from '@/lib/auth/session-store';

// createdAt must be RECENT, not a fixed literal: MemorySessionStore enforces the
// absolute TTL on access (mirroring the Redis TTL), so a fixture stamped in the
// past is expired the moment it is written and every assertion fails. Deriving it
// from Date.now() keeps the fixture valid whenever the suite runs.
const stored = (overrides: Partial<StoredSession> = {}): StoredSession => ({
  id: 's-1',
  createdAt: Date.now(),
  tokens: {
    accessToken: 'access-1',
    refreshToken: 'refresh-1',
    expiresAt: Date.now() + 300_000,
    refreshExpiresAt: Date.now() + 1_800_000,
  },
  presentedRefreshTokens: [],
  ...overrides,
});

describe('MemorySessionStore', () => {
  it('round-trips a session', async () => {
    const store = new MemorySessionStore();
    await store.put(stored());
    expect((await store.get('s-1'))?.tokens.refreshToken).toBe('refresh-1');
  });

  it('returns null for an unknown id rather than throwing', async () => {
    expect(await new MemorySessionStore().get('nope')).toBeNull();
  });

  it('overwrites on re-put, which is how a rotated token pair lands', async () => {
    const store = new MemorySessionStore();
    await store.put(stored());
    await store.put(
      stored({ tokens: { ...stored().tokens, refreshToken: 'refresh-2' } }),
    );
    expect((await store.get('s-1'))?.tokens.refreshToken).toBe('refresh-2');
  });

  it('deletes a session', async () => {
    const store = new MemorySessionStore();
    await store.put(stored());
    await store.delete('s-1');
    expect(await store.get('s-1')).toBeNull();
  });

  it('survives close() being called twice', async () => {
    const store = new MemorySessionStore();
    await store.close();
    await expect(store.close()).resolves.toBeUndefined();
  });
});

/**
 * The property that makes multi-instance rotation safe.
 *
 * Keycloak refresh tokens are single-use here, so two concurrent rotations would
 * present the same token twice and revoke the whole session family. Exactly one
 * caller may hold the lock at a time.
 */
describe('withRefreshLock', () => {
  it('grants the lock to the first caller', async () => {
    const store = new MemorySessionStore();
    const result = await store.withRefreshLock('s-1', async () => 'rotated');
    expect(result).toEqual({ acquired: true, value: 'rotated' });
  });

  it('refuses a second caller while the lock is held', async () => {
        const store = new MemorySessionStore();
        let release!: () => void;
        const gate = new Promise<void>((resolve) => {
          release = resolve;
        });

        const first = store.withRefreshLock('s-1', async () => {
          await gate;
          return 'rotated';
        });

        // Let the first caller actually take the lock before racing it.
        await new Promise((resolve) => setImmediate(resolve));

        // Start the second WITHOUT awaiting it: it queues behind the first's gate,
        // so awaiting here before `release` would deadlock (which it did — the
        // earlier version of this test timed out rather than failing an assertion).
        const second = store.withRefreshLock('s-1', async () => 'also rotated');

        release();
        const [firstResult, secondResult] = await Promise.all([first, second]);

        expect(firstResult.acquired).toBe(true);
        expect(secondResult.acquired).toBe(false);
        expect(secondResult.value).toBeNull();
      });

  it('admits exactly one winner among many concurrent callers', async () => {
    const store = new MemorySessionStore();
    let rotations = 0;

    const contenders = await Promise.all(
      Array.from({ length: 12 }, () =>
        store.withRefreshLock('s-1', async () => {
          rotations += 1;
          // Widen the window so every contender overlaps.
          await new Promise((resolve) => setTimeout(resolve, 5));
          return 'rotated';
        }),
      ),
    );

    const winners = contenders.filter((c) => c.acquired);
    expect(winners).toHaveLength(1);
    // If more than one had rotated, a single-use token would have been replayed.
    expect(rotations).toBe(1);
  });

  it('releases the lock after the callback throws, so one failure does not deadlock', async () => {
    const store = new MemorySessionStore();

    await expect(
      store.withRefreshLock('s-1', async () => {
        throw new Error('upstream 500');
      }),
    ).rejects.toThrow('upstream 500');

    // The next caller must not be locked out forever.
    const next = await store.withRefreshLock('s-1', async () => 'recovered');
    expect(next).toEqual({ acquired: true, value: 'recovered' });
  });

  it('releases the lock after the callback rejects, for the same reason', async () => {
    const store = new MemorySessionStore();
    await store
      .withRefreshLock('s-1', async () => {
        throw new Error('boom');
      })
      .catch(() => undefined);

    const next = await store.withRefreshLock('s-1', async () => 'ok');
    expect(next.acquired).toBe(true);
  });

  it('scopes the lock per session id', async () => {
    const store = new MemorySessionStore();
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });

    const held = store.withRefreshLock('s-1', async () => {
      await gate;
      return 'a';
    });
    await new Promise((resolve) => setImmediate(resolve));

    // A different session must not be blocked by s-1's lock.
    const other = await store.withRefreshLock('s-2', async () => 'b');

    release();
    await held;

    expect(other).toEqual({ acquired: true, value: 'b' });
  });

  it('grants the lock again once the holder has finished', async () => {
    const store = new MemorySessionStore();
    await store.withRefreshLock('s-1', async () => 'first');
    const second = await store.withRefreshLock('s-1', async () => 'second');
    expect(second).toEqual({ acquired: true, value: 'second' });
  });
});

/**
 * A store that is not safe across processes must be impossible to select in
 * production. This is the contract the factory relies on.
 */
describe('SessionStore contract', () => {
  it('MemorySessionStore is single-process only, and says so', () => {
    const store: SessionStore = new MemorySessionStore();
    // The capability flag is what the production assertion keys off.
    expect(store.isProcessLocal).toBe(true);
  });
});