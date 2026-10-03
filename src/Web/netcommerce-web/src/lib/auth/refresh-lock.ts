/**
 * The refresh-lock contract, shared by both store implementations.
 *
 * ## Why this is its own module
 *
 * `session-store.ts` declares the `SessionStore` interface, and
 * `redis-session-store.ts` implements it — so the Redis store already imports
 * from `session-store.ts`. Putting `RefreshLockGuard` in `session-store.ts` and
 * importing it back from the Redis store would create an import cycle.
 *
 * The types belong to the lock protocol rather than to any one store, so they get
 * their own home and both implementations depend on this instead of on each
 * other.
 */

/**
 * Handed to work running under a refresh lock.
 *
 * ## Why the caller must check
 *
 * Renewing a lease reduces the window in which it lapses; it cannot close it. A
 * process can be descheduled, hit a long GC pause, or lose its Redis connection
 * for longer than the TTL. So ownership is also verified ON DEMAND, immediately
 * before a rotation result is committed.
 *
 * That check is the actual safety property: it turns "I believe I still hold the
 * lock" into a question with an answer. A caller that presents a token after
 * losing its lease can double-spend a single-use refresh token, and Keycloak
 * responds by revoking the entire session family — logging that user out on every
 * device, with no error anywhere.
 */
export interface RefreshLockGuard {
  /**
   * True only while this process is still the recorded owner.
   *
   * Implementations must answer from the authoritative record rather than from a
   * locally cached flag, and must treat an unreachable backing store as NOT
   * held: absence of evidence is not evidence of ownership.
   */
  isHeld(): Promise<boolean>;

  /**
   * Throw unless the lease is still held.
   *
   * Preferred at a commit point, because it cannot be silently ignored the way a
   * boolean return can.
   */
  assertHeld(): Promise<void>;
}

/**
 * Thrown when work reaches a commit point without holding the lock.
 *
 * Named so callers can distinguish "I lost exclusivity" from a genuine rotation
 * failure — the two need opposite handling: this one means abandon the write,
 * while a rotation failure may leave the session usable.
 */
export class RefreshLockLostError extends Error {
  constructor() {
    super('refresh lock lease was lost before the result could be committed');
    this.name = 'RefreshLockLostError';
  }
}