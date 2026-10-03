/**
 * Pluggable session backing store.
 *
 * ## Why this exists
 *
 * The registry in `server-session.ts` was a `Map` on `globalThis`. That is correct
 * for one process and silently broken for two: behind a load balancer, a session
 * created on replica A is invisible to replica B, so the user appears logged out
 * at random depending on which replica answered. The same happens on every
 * restart and every rolling deploy.
 *
 * The failure is silent by construction — nothing throws, the app boots, health
 * checks pass, and users are simply logged out. So the store is an explicit
 * interface with two implementations and a production assertion, rather than a
 * comment asking someone to remember.
 *
 * ## The hard part is NOT storage — it is refresh
 *
 * Keycloak refresh tokens here are SINGLE-USE (`revokeRefreshToken=true`,
 * `max.reuse=0`). Presenting one twice — even from two different replicas a
 * second apart — revokes the ENTIRE Keycloak session family: every device, not
 * just this one. The single-flight promise in `TokenStore` only covers one
 * process. Across replicas it is worthless.
 *
 * So a distributed session store must ALSO provide mutual exclusion around
 * rotation. That is what `withRefreshLock` is for. Without it, "just use Redis"
 * trades a visible logout bug for a silent mass-logout bug, which is worse.
 */

import type { Session } from '@/lib/auth/token-store';
import {
  RefreshLockLostError,
  type RefreshLockGuard,
} from '@/lib/auth/refresh-lock';

export interface StoredSession {
  /** Opaque id handed to the browser in the httpOnly cookie. */
  id: string;
  /** Epoch ms the session was created. Drives the absolute TTL. */
  createdAt: number;
  /**
   * The token pair as of the last write.
   *
   * Stored rather than reconstructed: a replica that has not seen the login
   * still needs the refresh token to keep the session alive, and the tokens are
   * the session.
   */
  tokens: Session;
  /**
   * Refresh tokens already presented to the backend.
   *
   * Persisted, because the replay guard must survive a replica restart. An
   * in-memory set would forget, and the next rotation would replay a consumed
   * token — the exact failure this design exists to prevent.
   */
  presentedRefreshTokens: string[];
}

export interface SessionStore {
  /**
   * True when this store keeps sessions in THIS process only.
   *
   * The production assertion refuses to boot on a store where this is true: two
   * replicas behind a load balancer would each hold different sessions, so users
   * would be logged out at random depending on which replica answered. Nothing
   * throws in that configuration — it just fails, silently, forever.
   */
  readonly isProcessLocal: boolean;

  get(id: string): Promise<StoredSession | null>;
  put(session: StoredSession): Promise<void>;
  delete(id: string): Promise<void>;
  /**
   * Run `fn` with exclusive rights to rotate this session's tokens.
   *
   * MUST be exclusive across processes. Returns a result marker so the caller can
   * distinguish "I did the rotation" from "someone else did, use their result"
   * from "lock unavailable, do not attempt" — collapsing these would either
   * replay a token or drop a legitimate refresh.
   *
   * `fn` receives a guard. The implementation MUST keep exclusive rights for the
   * whole of `fn` — including past the point where an implementation's own TTL
   * would lapse, which a Redis implementation achieves by renewing the lease
   * while `fn` runs. The caller MUST consult the guard immediately before
   * committing: a lease can still be lost to a descheduled process or a dropped
   * connection, and committing a rotation after losing it can double-spend a
   * single-use refresh token.
   */
  withRefreshLock<T>(
    id: string,
    fn: (guard: RefreshLockGuard) => Promise<T>,
  ): Promise<{ acquired: true; value: T } | { acquired: false; value: null }>;
  /** Release any resources. Safe to call more than once. */
  close(): Promise<void>;
}

/* ------------------------------------------------------------------ *
 * In-memory implementation — development and single-process only.
 * ------------------------------------------------------------------ */

/**
 * Process-local store.
 *
 * `withRefreshLock` here only excludes callers inside this process, which is the
 * honest limit of an in-memory store. It is NOT safe across replicas, which is
 * exactly why `assertProductionConfiguration` refuses to run this in production.
 */
export class MemorySessionStore implements SessionStore {
  /**
   * Always true. This is the flag the production assertion refuses to boot on.
   */
  readonly isProcessLocal = true;

  private readonly sessions = new Map<string, StoredSession>();

  /** Lock table, keyed by session id. Mirrors the Redis SET NX behaviour. */
  private readonly locks = new Map<string, Promise<unknown>>();

  /**
   * Absolute lifetime, mirroring the Redis TTL.
   *
   * Redis expires keys on its own, so nothing has to sweep there. An in-process
   * Map has no such mechanism, and dropping the lazy sweep left this growing
   * without bound for the lifetime of the process — a slow leak in dev, and a
   * memory-exhaustion bug for anyone who ran a single-replica "production" with
   * SESSION_STORE=memory.
   */
  constructor(private readonly ttlMs = 8 * 60 * 60 * 1000) {}

  /** Drop entries past the absolute TTL. Called on every access. */
  private sweep(now: number): void {
    for (const [id, session] of this.sessions) {
      if (now - session.createdAt >= this.ttlMs) this.sessions.delete(id);
    }
  }

  async get(id: string): Promise<StoredSession | null> {
    this.sweep(Date.now());
    return this.sessions.get(id) ?? null;
  }

  async put(session: StoredSession): Promise<void> {
    this.sweep(Date.now());
    this.sessions.set(session.id, session);
  }

  async delete(id: string): Promise<void> {
    this.sessions.delete(id);
  }

  async withRefreshLock<T>(
    id: string,
    fn: (guard: RefreshLockGuard) => Promise<T>,
  ): Promise<{ acquired: true; value: T } | { acquired: false; value: null }> {
    // Queue behind an existing holder rather than racing it. Rejections are
    // swallowed here so one failed rotation does not poison the chain for
    // everyone who waits behind it.
    const held = this.locks.get(id);
    if (held) {
      await held.catch(() => undefined);
      return { acquired: false, value: null };
    }

    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    this.locks.set(id, gate);

    // The lock table entry is removed only in the `finally` below, and this
    // closure reads it at the moment of the call — so while `fn` is running the
    // entry is present and we are the holder. There is no TTL here, so unlike the
    // Redis store there is no lease to lose; the guard exists to keep the two
    // implementations interchangeable.
    const guard: RefreshLockGuard = {
      isHeld: async () => this.locks.get(id) === gate,
      assertHeld: async () => {
        if (this.locks.get(id) !== gate) throw new RefreshLockLostError();
      },
    };

    try {
      return { acquired: true, value: await fn(guard) };
    } finally {
      this.locks.delete(id);
      release();
    }
  }

  async close(): Promise<void> {
    this.sessions.clear();
    this.locks.clear();
  }

  /* --- test seams, mirroring server-session.ts ---------------------- */
  __size(): number {
    return this.sessions.size;
  }
  __clear(): void {
    this.sessions.clear();
  }
}