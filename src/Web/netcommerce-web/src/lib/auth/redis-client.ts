import type { RedisClientType } from 'redis';

import type { RedisLike } from '@/lib/auth/redis-session-store';
import { readConfig } from '@/lib/config';

/**
 * The REAL Redis client.
 *
 * ## Why this file exists
 *
 * `RedisSessionStore` and `createSessionStore` were both written and neither was
 * reachable from the running app: the factory takes an injected `redisClient`,
 * and nothing anywhere constructed one. So in production either the guard fired
 * at the first request or — with REDIS_URL set — every session call threw
 * "no Redis client was provided". The Redis path existed only inside tests.
 *
 * This module is the missing production wiring.
 *
 * ## Node-redis v5 API mapping (verified against the installed 5.12.1)
 *
 * `RedisLike` is the narrow surface the store depends on. The node-redis client
 * satisfies it structurally EXCEPT for `eval`, whose v5 signature is
 * `EVAL(script, { keys, arguments })` — matching `RedisLike.eval`. The adapter
 * therefore exists to (a) build the client from REDIS_URL and (b) pin the
 * connection behaviour the session store depends on.
 *
 * ## Connection policy
 *
 * `disableOfflineQueue: true` is deliberate. With the default, commands issued
 * while the socket is down are buffered and replayed on reconnect — so a logout
 * or a token write could land minutes late, and a stale session write could
 * resurrect a session. Failing fast instead means the request errors while the
 * fault is visible, which is the posture a session layer wants.
 *
 * Single shared client on globalThis: node-redis multiplexes over one socket and
 * reconnects on its own, so one client per process is correct — and creating one
 * per request would leak sockets.
 */

type SharedClient = RedisClientType;

const globalRef = globalThis as unknown as {
  __ncRedisClient?: SharedClient;
  __ncRedisConnecting?: Promise<SharedClient>;
};

/**
 * Build the adapter around a node-redis client.
 *
 * Exported so the wiring is testable with a stub client, and so the exact
 * surface the store needs is asserted rather than assumed.
 */
export function adaptRedisClient(client: SharedClient): RedisLike {
  return {
    get: (key) => client.get(key),
    set: (key, value, options) => client.set(key, value, options ?? {}),
    del: (key) => client.del(key),
    eval: (script, options) =>
      client.eval(script, { keys: options.keys, arguments: options.arguments }),
  };
}

/**
 * Return the shared client, creating and connecting it on first use.
 *
 * Concurrent callers share ONE connection attempt: node-redis clients are not
 * safe to connect twice, and two simultaneous logins must not race into two
 * sockets.
 */
export async function getRedisClient(): Promise<RedisLike> {
  if (globalRef.__ncRedisClient) {
    return adaptRedisClient(globalRef.__ncRedisClient);
  }

  const config = readConfig();
  const url = process.env.REDIS_URL?.trim();

  if (!url) {
    throw new Error(
      'REDIS_URL is not set, so no Redis session store can be created. ' +
        'Set it (the AppHost injects it from the redis resource) or set ' +
        'SESSION_STORE=memory outside production.',
    );
  }

  globalRef.__ncRedisConnecting ??= (async () => {
    // Imported lazily so a deployment that never selects the redis store does not
    // pay for the module, and so importing this file stays safe in tests that
    // have no redis dependency installed.
    const { createClient } = await import('redis');

    const client: SharedClient = createClient({
      url,
      // Fail fast rather than buffering commands across a reconnect — see the
      // module doc.
      disableOfflineQueue: true,
    }) as unknown as SharedClient;

    // Without a listener, an 'error' event on a node-redis client is an
    // unhandled error event that takes the process down. This is mandatory, not
    // diagnostic noise.
    client.on('error', (cause: unknown) => {
      console.error('[redis] session store connection error', cause);
    });

    await client.connect();
    globalRef.__ncRedisClient = client;
    return client;
  })();

  try {
    const client = await globalRef.__ncRedisConnecting;
    return adaptRedisClient(client);
  } catch (cause) {
    // Clear the memoized failure so a later request can retry rather than
    // inheriting this one forever.
    globalRef.__ncRedisConnecting = undefined;
    throw cause;
  } finally {
    if (globalRef.__ncRedisClient) {
      globalRef.__ncRedisConnecting = undefined;
    }
  }
}

/** True when a client has been established. Used by tests and diagnostics. */
export function __hasRedisClient(): boolean {
  return Boolean(globalRef.__ncRedisClient);
}

/**
 * Synchronous view of an ALREADY-CONNECTED client, for callers that cannot await.
 *
 * The session store is constructed synchronously inside the request path, so it
 * cannot await a connection — the client is established once at startup instead
 * (see src/instrumentation.ts). Returns null when no client has been established
 * yet, which is the signal the factory turns into a loud error rather than a
 * silent fallback to process memory.
 *
 * Named with the __ prefix because it is a lifecycle accessor, not application
 * API: nothing should build a session store by hand.
 */
export function __getEstablishedRedisClient(): RedisLike | null {
  if (!globalRef.__ncRedisClient) return null;
  return adaptRedisClient(globalRef.__ncRedisClient);
}

/** Test seam: drop the shared client so the next call reconnects. */
export async function __resetRedisClientForTests(): Promise<void> {
  const client = globalRef.__ncRedisClient;
  globalRef.__ncRedisClient = undefined;
  globalRef.__ncRedisConnecting = undefined;
  if (client) {
    await client.quit().catch(() => undefined);
  }
}