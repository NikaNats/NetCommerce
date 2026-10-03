/**
 * Session store selection and the production guard.
 *
 * ## The failure this prevents
 *
 * With a process-local store behind a load balancer, nothing throws. The app
 * boots, every health check passes, and a user is logged out whenever their next
 * request lands on a replica that has not seen their session. Support sees
 * "users randomly logged out" with no error anywhere. That is strictly worse than
 * a boot failure, and it is why the check below is an assertion rather than a
 * comment.
 *
 * ## Escape hatch
 *
 * `SESSION_STORE=memory` is honoured ONLY outside production. Someone running a
 * deliberate single-replica deployment (a laptop, a fixed container) is not
 * forced into standing up Redis. In production the override is ignored — there
 * is no configuration that produces a silently-broken multi-replica session
 * layer.
 */

import { MemorySessionStore, type SessionStore } from '@/lib/auth/session-store';
import { RedisSessionStore, type RedisLike } from '@/lib/auth/redis-session-store';
import { SESSION_ABSOLUTE_TTL_MS } from '@/lib/auth/session-store-ttl';
import { __getEstablishedRedisClient } from '@/lib/auth/redis-client';

export class SessionStoreConfigError extends Error {
  constructor(detail: string) {
    super(`Invalid session store configuration: ${detail}`);
    this.name = 'SessionStoreConfigError';
  }
}

export type SessionStoreKind = 'memory' | 'redis';

export interface ResolveOptions {
  env?: EnvLike;
  /** Injected so tests need no live Redis. */
  redisClient?: RedisLike;
}

/**
 * Next augments NodeJS.ProcessEnv with a required NODE_ENV, so a plain object
 * literal is not assignable. Accept a looser shape: these functions only read
 * string variables, and the test suite should not have to satisfy the
 * augmentation to pass a fixture.
 */
export type EnvLike = Record<string, string | undefined>;

/**
 * Is this a production deployment?
 *
 * Deliberately NOT `NODE_ENV === 'production'`. Exact match let through
 * 'Production', 'prod', and — worst — an UNSET NODE_ENV, which is the realistic
 * container default. A container that forgets NODE_ENV would run a
 * process-local session store while the operator believed it was configured, and
 * the symptom is users randomly logged out with no error anywhere.
 *
 * So anything not recognisably development or test is treated as production.
 * Failing closed is right for a session layer: the false-positive cost is a
 * refused start with a clear message, versus a silent production outage.
 */
export function isProductionEnv(env: EnvLike = process.env): boolean {
  const value = (env.NODE_ENV ?? '').trim().toLowerCase();
  return value !== 'development' && value !== 'dev' && value !== 'test';
}

export function resolveSessionStoreKind(env: EnvLike = process.env): SessionStoreKind {
  const requested = (env.SESSION_STORE ?? '').trim().toLowerCase();
  const url = (env.REDIS_URL ?? '').trim();

  if (requested === 'memory') return 'memory';
  if (requested === 'redis') return 'redis';

  // Unset: infer from REDIS_URL so a deployment that already provides Redis gets
  // a shared store without extra configuration.
  return url ? 'redis' : 'memory';
}

/**
 * Refuse to run production on a process-local store.
 *
 * Called once at startup. Throwing here stops the rollout rather than shipping a
 * session layer that fails intermittently under load.
 */
export function assertProductionSessionStore(
  kind: SessionStoreKind,
  env: EnvLike = process.env,
): void {
  const isProduction = isProductionEnv(env);

  if (isProduction && kind === 'memory') {
    throw new SessionStoreConfigError(
      'a process-local session store cannot serve more than one replica. ' +
        'Behind a load balancer users would be signed out at random. ' +
        'Set REDIS_URL (or SESSION_STORE=redis) to use a shared store. ' +
        'SESSION_STORE=memory is ignored in production by design.',
    );
  }

  if (kind === 'redis' && !(env.REDIS_URL ?? '').trim()) {
    throw new SessionStoreConfigError('SESSION_STORE=redis requires REDIS_URL to be set');
  }
}

/**
 * Build the store the environment calls for.
 *
 * Kept separate from the singleton so the choice is testable without touching
 * global state or needing a Redis server.
 */
export function createSessionStore(options: ResolveOptions = {}): SessionStore {
  const env = options.env ?? process.env;
  const kind = resolveSessionStoreKind(env);

  assertProductionSessionStore(kind, env);

  if (kind === 'memory') {
    return new MemorySessionStore();
  }

  // The redis path previously REQUIRED an injected client and threw without one,
  // so production could never obtain a shared store: unset REDIS_URL tripped the
  // guard, and set REDIS_URL made every session call throw. getRedisClient() is the
  // production wiring; the injected client remains for tests.
  const client = options.redisClient ?? requireRedisClientSync();

  return new RedisSessionStore({
    client,
    ttlSeconds: Math.ceil(SESSION_ABSOLUTE_TTL_MS / 1000),
  });
}

/**
 * Obtain the shared Redis client.
 *
 * createSessionStore is SYNCHRONOUS because it runs inside the session read path,
 * while connecting is asynchronous. So the client is created eagerly at startup
 * (see src/instrumentation.ts) and this only reads the already-established
 * instance.
 *
 * Throws rather than silently degrading: a production session store that quietly
 * falls back to process memory is the exact silent-logout bug this change exists
 * to prevent.
 */
function requireRedisClientSync(): RedisLike {
  const established = __getEstablishedRedisClient();
  if (established) return established;

  throw new SessionStoreConfigError(
    'a Redis session store was selected but no client is connected. ' +
      'This is reached when the app starts without establishing the Redis client ' +
      'at startup. Check that REDIS_URL is set and reachable.',
  );
}

/**
 * Process-wide singleton.
 *
 * On globalThis rather than module scope, for the same reason the original
 * registry was: route handlers get separate module instances in this Next
 * version, so a bare module-level binding would give /callback and /basket
 * different stores.
 */
const globalRef = globalThis as unknown as {
  __ncSessionStore?: SessionStore;
};

export function getSessionStore(): SessionStore {
  globalRef.__ncSessionStore ??= createSessionStore();
  return globalRef.__ncSessionStore;
}

/** Test seam. Not reachable from app code — see `__resetSessionStoreForTests`. */
export function __setSessionStoreForTests(store: SessionStore | undefined): void {
  globalRef.__ncSessionStore = store;
}