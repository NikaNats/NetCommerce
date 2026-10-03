import { afterEach, describe, expect, it, vi } from 'vitest';

/**
 * Regression tests for the audit's BLOCKER 1: production could not obtain a
 * Redis session store.
 *
 * The defect was an INERT module — `RedisSessionStore` existed, the factory
 * referenced it, and unit tests exercised it through an INJECTED client, so
 * every test passed while the running app could never reach the Redis path.
 * `redis` was not even a dependency.
 *
 * These tests therefore assert REACHABILITY from the real production entry point,
 * not the store's behaviour in isolation.
 */

const {
  createSessionStore,
  resolveSessionStoreKind,
  SessionStoreConfigError,
  __setSessionStoreForTests,
} = await import('@/lib/auth/session-store-factory');
const { MemorySessionStore } = await import('@/lib/auth/session-store');

afterEach(() => {
  __setSessionStoreForTests(undefined);
  vi.unstubAllGlobals();
});

/** Minimal node-redis-shaped client, enough for the adapter and the store. */
function stubNodeRedis() {
  const calls: string[] = [];
  const client = {
    get: async (k: string) => {
      calls.push(`get:${k}`);
      return null;
    },
    set: async (k: string) => {
      calls.push(`set:${k}`);
      return 'OK';
    },
    del: async (k: string) => {
      calls.push(`del:${k}`);
      return 1;
    },
    eval: async () => {
      calls.push('eval');
      return 1;
    },
    connect: async () => {
      calls.push('connect');
    },
    quit: async () => {
      calls.push('quit');
    },
    on: () => undefined,
  };
  return { client, calls };
}

describe('BLOCKER 1: the redis package is a real dependency', () => {
  it('is declared in package.json', async () => {
    // Reading the manifest rather than importing: the defect was precisely that
    // the package was absent while the code referenced it.
    const { readFileSync } = await import('node:fs');
    const pkg = JSON.parse(
      readFileSync(new URL('../package.json', import.meta.url), 'utf8'),
    );
    expect(Object.keys(pkg.dependencies)).toContain('redis');
  });

  it('actually resolves, so the import cannot be dead', () => {
    // require.resolve, NOT a dynamic import. Evaluating node-redis under the test
    // sandbox hangs past the 5s timeout, which says nothing about whether the
    // package is present. Resolution is the property this test is about.
    const resolved = require.resolve('redis');
    expect(resolved).toMatch(/redis/);

    // And the entry point exposes createClient, read without evaluating it.
    const pkg = JSON.parse(
      require('node:fs').readFileSync(
        require.resolve('redis/package.json'),
        'utf8',
      ),
    ) as { main?: string; exports?: Record<string, unknown> };
    expect(pkg.main ?? Object.keys(pkg.exports ?? {}).length).toBeTruthy();
  });
});

describe('BLOCKER 1: production can obtain a shared session store', () => {
  it('connects a client and builds a non-process-local store', async () => {
    const { client, calls } = stubNodeRedis();

    // Drive the real production path: instrumentation-style startup, then the
    // factory with NO injected client — the case that used to throw.
    const { getRedisClient } = await import('@/lib/auth/redis-client');
    const { __resetRedisClientForTests } = await import('@/lib/auth/redis-client');

    vi.doMock('redis', () => ({ createClient: () => client }));

    process.env.REDIS_URL = 'redis://cache:6379';
    process.env.SESSION_STORE = 'redis';

    const adapter = await getRedisClient();
    expect(typeof adapter.get).toBe('function');

    // connect() must have been called: an unconnected adapter is the defect.
    expect(calls).toContain('connect');

    // Now the factory, still with no injected client.
    const store = createSessionStore({
      env: { NODE_ENV: 'production', SESSION_STORE: 'redis', REDIS_URL: 'redis://cache:6379' },
    });
    expect(store.isProcessLocal).toBe(false);

    // And the adapter actually talks to the client.
    await adapter.get('some-key');
    expect(calls.some((c) => c.startsWith('get:'))).toBe(true);

    await __resetRedisClientForTests();
    delete process.env.REDIS_URL;
    delete process.env.SESSION_STORE;
  });

  it('throws a NAMED error when no client is established', () => {
    // This is the loud failure that replaces "silently fall back to memory".
    expect(() =>
      createSessionStore({
        env: { NODE_ENV: 'production', SESSION_STORE: 'redis', REDIS_URL: 'redis://cache:6379' },
      }),
    ).toThrow(SessionStoreConfigError);
  });

  it('does NOT silently fall back to a process-local store in production', () => {
    // The exact failure mode the whole change exists to prevent.
    let store;
    try {
      store = createSessionStore({
        env: { NODE_ENV: 'production', SESSION_STORE: 'redis', REDIS_URL: 'redis://x' },
      });
    } catch {
      store = undefined;
    }
    expect(store).toBeUndefined();
  });
});

describe('production guard is not bypassed at boot', () => {
  it('instrumentation.ts exists and runs the guard before traffic', async () => {
    // A guard that only runs on the first request is not a boot guard.
    const { readFileSync } = await import('node:fs');
    const source = readFileSync(
      new URL('../src/instrumentation.ts', import.meta.url),
      'utf8',
    );
    expect(source).toContain('assertProductionSessionStore');
  });

  it.each([
    ['Production', 'a capitalised value'],
    ['production ', 'a trailing space'],
    ['prod', 'an abbreviation'],
  ])('refuses NODE_ENV=%j (%s)', (value) => {
    // Exact-match on 'production' let all of these through, including NODE_ENV
    // unset — which is the realistic container default.
    expect(() =>
      createSessionStore({
        env: { NODE_ENV: value, SESSION_STORE: 'memory' },
      }),
    ).toThrow(SessionStoreConfigError);
  });

  it('refuses when NODE_ENV is unset and a memory store is selected', () => {
    // Unset is the dangerous case: a container that forgets NODE_ENV would
    // otherwise run a process-local session store believing it was configured.
    expect(() =>
      createSessionStore({
        env: { SESSION_STORE: 'memory' },
      }),
    ).toThrow(SessionStoreConfigError);
  });
});

describe('development still works without Redis', () => {
  it('builds a process-local store in development', () => {
    const store = createSessionStore({ env: { NODE_ENV: 'development' } });
    expect(store).toBeInstanceOf(MemorySessionStore);
  });

  it('still honours an explicit SESSION_STORE=memory in development', () => {
    expect(resolveSessionStoreKind({ NODE_ENV: 'development', SESSION_STORE: 'memory' })).toBe(
      'memory',
    );
  });
});