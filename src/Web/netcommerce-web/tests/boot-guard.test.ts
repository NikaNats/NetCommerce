import { afterEach, describe, expect, it } from 'vitest';

/**
 * Assert the boot guard by CALLING register() — not by starting a server.
 *
 * ## Why not spawn the app
 *
 * scripts/check-boot-guard.sh did start the standalone build, and it reported
 * three FAILURES that were all false. In this sandbox `.next/standalone/server.js`
 * never binds a port and writes an empty log — including with NO session
 * environment at all — so the harness could not tell "the guard refused to start"
 * apart from "the server never started". A check that reports a failure it cannot
 * interpret is worse than no check: it looks like evidence.
 *
 * Next's own docs state register() "is called once when a new Next.js server
 * instance is initiated", and Next 15+ builds it with no experimental flag. So
 * the contract under test is register()'s behaviour, exercised directly.
 *
 * ## What this does NOT prove
 *
 * That Next invokes register() in the deployed standalone server. That needs a
 * real boot in CI where the standalone server actually runs; see the note in
 * instrumentation.ts. This file pins the LOGIC that the invocation feeds.
 */

const ORIGINAL_ENV = { ...process.env };

afterEach(() => {
  process.env = { ...ORIGINAL_ENV };
});

async function registerWith(env: Record<string, string | undefined>) {
  for (const key of ['NODE_ENV', 'SESSION_STORE', 'REDIS_URL']) {
    delete process.env[key];
  }
  Object.assign(process.env, env);

  // Fresh module graph per call, so the shared redis client memo is not reused.
  const mod = await import(
    `@/instrumentation?case=${Math.random().toString(36).slice(2)}`
  );
  return mod.register();
}

describe('boot guard: refuses a process-local store outside development', () => {
  it.each([
    ['NODE_ENV=production', 'production'],
    ['NODE_ENV unset', undefined],
    ['NODE_ENV=prod', 'prod'],
    ['NODE_ENV=Production', 'Production'],
  ])('refuses with %s', async (_label, nodeEnv) => {
    await expect(
      registerWith({ NODE_ENV: nodeEnv, SESSION_STORE: 'memory' }),
    ).rejects.toThrow(/process-local session store/i);
  });
});

describe('boot guard: allows a local store only in development/test', () => {
  it.each([
    ['development', 'development'],
    ['dev', 'dev'],
    ['test', 'test'],
  ])('allows %s', async (_label, nodeEnv) => {
    // Must NOT throw: local development without Redis has to keep working.
    await expect(
      registerWith({ NODE_ENV: nodeEnv, SESSION_STORE: 'memory' }),
    ).resolves.toBeUndefined();
  });
});

describe('boot guard: redis path does not silently degrade', () => {
  it('refuses when the redis store is selected but no client can connect', async () => {
    // An unreachable REDIS_URL must fail the boot, not fall back to memory.
    // Port 6399 is closed, so connect() rejects.
    await expect(
      registerWith({
        NODE_ENV: 'production',
        SESSION_STORE: 'redis',
        REDIS_URL: 'redis://127.0.0.1:6399',
      }),
    ).rejects.toThrow();
  });

  it('names the missing variable when REDIS_URL is absent', async () => {
    await expect(
      registerWith({ NODE_ENV: 'production', SESSION_STORE: 'redis' }),
    ).rejects.toThrow(/REDIS_URL/);
  });
});