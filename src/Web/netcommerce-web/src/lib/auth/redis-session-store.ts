/**
 * Redis-backed session store.
 *
 * ## Why Redis and not a database
 *
 * The AppHost already runs Redis for the API's basket and distributed locks, so
 * reusing it adds no new infrastructure. Sessions are read on every request and
 * written only on rotation, which is exactly Redis's shape. A relational store
 * would also work but buys nothing here.
 *
 * ## The distributed lock is the point
 *
 * `SET key value NX PX ttl` is the primitive. Only one replica can create the
 * key, so only one replica rotates. That matters because Keycloak refresh tokens
 * here are single-use: a second rotation within the lock window would present a
 * consumed token and revoke the entire session family.
 *
 * The lock is released with a compare-and-delete (Lua) rather than a plain DEL.
 * A plain DEL would let a replica whose lock had already expired delete the lock
 * a DIFFERENT replica now holds, opening a second concurrent rotation — the exact
 * bug the lock exists to prevent.
 *
 * ## Tokens are stored as plain JSON
 *
 * They live in Redis alongside the API's own basket data. That is a deliberate
 * trust decision: anything that can read Redis can read a live refresh token.
 * The mitigation is network-level — Redis bound to the internal network, never
 * exposed, with the API's own credentials. TLS/auth belong on the connection
 * string in production. Storing them is not a new exposure relative to the API,
 * which already keeps bearer tokens in Redis-backed state.
 */

import type { SessionStore, StoredSession } from '@/lib/auth/session-store';
import {
  RefreshLockLostError,
  type RefreshLockGuard,
} from '@/lib/auth/refresh-lock';

/** Minimal surface of the `redis` v4 client this store depends on. */
export interface RedisLike {
  get(key: string): Promise<string | null>;
  set(
    key: string,
    value: string,
    options?: { NX?: boolean; PX?: number; EX?: number },
  ): Promise<string | null>;
  del(key: string | string[]): Promise<number>;
  eval(
    script: string,
    options: { keys: string[]; arguments: string[] },
  ): Promise<unknown>;
}

/**
 * Release a lock ONLY if we still own it.
 *
 * Returns 1 when deleted, 0 when the key was absent or held by someone else. The
 * second case is the dangerous one: a naive `DEL` would delete another replica's
 * lock and permit a second concurrent rotation of a single-use token.
 *
 * NOTE ON THE NAME: this is Redis's server-side `EVAL`, which runs Lua inside
 * Redis' own sandbox — a Lua interpreter with no filesystem, network, or shell
 * access, and no ability to touch the host process. It is NOT JavaScript `eval`
 * and takes no user input: the only argument is a value this process generated.
 * `EVALSHA`/`EVAL` with a constant script is the documented way to do an atomic
 * compare-and-delete, because a GET-then-DEL round trip from the client is racy.
 */
const RELEASE_IF_OWNER = `
if redis.call('GET', KEYS[1]) == ARGV[1] then
  return redis.call('DEL', KEYS[1])
else
  return 0
end
`;

/**
 * Extend the lease ONLY if we still own it.
 *
 * The renewal half of the fix for the audit's BLOCKER 2. A plain `PEXPIRE` would
 * be the bug all over again: a holder whose lease had already been stolen would
 * extend the NEW owner's lock, keeping it stuck until that owner's own TTL
 * lapsed. The owner check makes renewal safe to run on a timer without
 * coordination.
 *
 * Returns 1 when extended, 0 when the lease is gone — which is the signal the
 * caller needs to abandon the rotation before it presents a token.
 */
const RENEW_IF_OWNER = `
if redis.call('GET', KEYS[1]) == ARGV[1] then
  return redis.call('PEXPIRE', KEYS[1], ARGV[2])
else
  return 0
end
`;

/**
 * The guard types live in ./refresh-lock rather than here: the Redis store
 * already imports this module, so declaring them here and importing them back
 * would create a cycle.
 */

export interface RedisSessionStoreOptions {
  client: RedisLike;
  /** Prefix so this store's keys never collide with the API's. */
  keyPrefix?: string;
  /** Absolute session lifetime. Also the Redis TTL, so orphans self-expire. */
  ttlSeconds: number;
  /**
   * How long a refresh lock is held before Redis expires it.
   *
   * Must comfortably exceed the worst-case token round trip. Too short and a slow
   * rotation loses its lock and a second replica rotates concurrently; too long
   * and a crashed holder blocks refreshes for that long.
   */
  lockTtlMs?: number;
}

const DEFAULT_LOCK_TTL_MS = 10_000;

export class RedisSessionStore implements SessionStore {
  /** False: this store is shared, which is what makes it production-legal. */
  readonly isProcessLocal = false;

  private readonly client: RedisLike;
  private readonly prefix: string;
  private readonly ttlSeconds: number;
  private readonly lockTtlMs: number;

  constructor(options: RedisSessionStoreOptions) {
    this.client = options.client;
    this.prefix = options.keyPrefix ?? 'netcommerce:web:session';
    this.ttlSeconds = options.ttlSeconds;
    this.lockTtlMs = options.lockTtlMs ?? DEFAULT_LOCK_TTL_MS;
  }

  private sessionKey(id: string): string {
    return `${this.prefix}:${id}`;
  }

  private lockKey(id: string): string {
    return `${this.prefix}:lock:${id}`;
  }

  async get(id: string): Promise<StoredSession | null> {
    const raw = await this.client.get(this.sessionKey(id));
    if (!raw) return null;

    try {
      const parsed = JSON.parse(raw) as StoredSession;
      // Defend against a truncated, hand-edited, or older-format value. A session
      // without a usable refresh token is not a session; returning null makes the
      // caller log out cleanly instead of failing deep in the refresh path.
      if (!parsed?.tokens?.refreshToken || !parsed?.id) return null;

      // presentedRefreshTokens is read with .includes() by the rotation path, so a
      // record missing it would throw a TypeError — a 500 rather than the
      // "treat as absent" posture this function already takes for corrupt JSON.
      // Default it rather than reject: an absent set is safe (nothing has been
      // recorded as presented), whereas rejecting would sign the user out.
      if (!Array.isArray(parsed.presentedRefreshTokens)) {
        parsed.presentedRefreshTokens = [];
      }

      return parsed;
    } catch {
      // Corrupt JSON: treat as absent. The TTL will clear the key anyway.
      return null;
    }
  }

  async put(session: StoredSession): Promise<void> {
    // The Redis TTL is the session's absolute expiry, so a crashed process
    // leaves nothing behind and no sweeper is needed.
    await this.client.set(
      this.sessionKey(session.id),
      JSON.stringify(session),
      { EX: this.ttlSeconds },
    );
  }

  async delete(id: string): Promise<void> {
    await this.client.del(this.sessionKey(id));
  }

  async withRefreshLock<T>(
    id: string,
    fn: (guard: RefreshLockGuard) => Promise<T>,
  ): Promise<{ acquired: true; value: T } | { acquired: false; value: null }> {
    // A random owner token, so the release script can tell "my lock" from
    // "someone else's lock that happens to share the key".
    const owner = `${process.pid}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
    const key = this.lockKey(id);

    const acquired = await this.client.set(key, owner, {
      NX: true,
      PX: this.lockTtlMs,
    });

    if (acquired !== 'OK') {
      return { acquired: false, value: null };
    }

    /**
     * Authoritative ownership check.
     *
     * Reads the recorded owner rather than trusting a local flag: a renewal may
     * have failed while we were descheduled, and the whole point is to answer
     * "is this still mine" at the moment the caller asks.
     */
    const isHeld = async (): Promise<boolean> => {
      try {
        return (await this.client.get(key)) === owner;
      } catch {
        // A Redis failure is NOT evidence the lock is still ours. Treating an
        // unreachable Redis as "held" would let a caller commit a rotation whose
        // lease may already be gone, which is the double-spend this guards.
        return false;
      }
    };

    const guard: RefreshLockGuard = {
      isHeld,
      assertHeld: async () => {
        if (!(await isHeld())) throw new RefreshLockLostError();
      },
    };

    // Renewal watchdog.
    //
    // Renew at a third of the TTL so two consecutive failures still leave time
    // to notice before the lease actually lapses. Without this, any rotation
    // slower than the TTL lets a second replica in — and since these refresh
    // tokens are single-use, the second presentation revokes the entire session
    // family for that user on every device.
    //
    // A stopped timer that never fired is harmless: the TTL still bounds the
    // lock, which is exactly the old (unsafe) behaviour, so a crash or a stalled
    // event loop degrades to the previous failure mode rather than to a stuck
    // lock nobody releases.
    const renewEveryMs = Math.max(50, Math.floor(this.lockTtlMs / 3));
    const renewTimer = setInterval(() => {
      // Fire-and-forget: a failed renewal is detected by isHeld() at the commit
      // point, and awaiting here would race the work it is meant to protect.
      void this.client
        .eval(RENEW_IF_OWNER, {
          keys: [key],
          arguments: [owner, String(this.lockTtlMs)],
        })
        .catch(() => undefined);
    }, renewEveryMs);

    // Never hold the event loop open for a lock heartbeat. Without this a
    // process could refuse to exit while a renewal is pending.
    renewTimer.unref?.();

    try {
      return { acquired: true, value: await fn(guard) };
    } finally {
      clearInterval(renewTimer);
      // Compare-and-delete, and never let a failed release mask the caller's own
      // result or error.
      await this.client
        .eval(RELEASE_IF_OWNER, { keys: [key], arguments: [owner] })
        .catch(() => undefined);
    }
  }

  async close(): Promise<void> {
    // Nothing to release: keys are owned by Redis, not by this process.
  }
}