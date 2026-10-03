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
      // Defend against a truncated or hand-edited value: a session without a
      // usable refresh token is not a session. Returning null makes the caller
      // log the user out cleanly instead of failing on a missing field later.
      if (!parsed?.tokens?.refreshToken || !parsed?.id) return null;
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
    fn: () => Promise<T>,
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

    try {
      return { acquired: true, value: await fn() };
    } finally {
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