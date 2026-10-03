/**
 * Startup wiring.
 *
 * Next calls `register()` once per server process, before the first request. Two
 * things happen here, both of which must happen BEFORE traffic rather than on it:
 *
 *  1. The session store's production guard runs. Previously it only executed when
 *     a request arrived, so a misconfigured deployment passed its health checks
 *     and failed on the first login. `assertProductionSessionStore` now runs at
 *     boot, and throws — which fails the deploy.
 *
 *  2. The Redis client connects. The session store is constructed synchronously
 *     inside the request path, so it cannot await a connection; the client is
 *     therefore established here, once, and shared for the process lifetime.
 *
 * ## Why a failure here is fatal rather than logged
 *
 * A session layer that cannot reach its store would either fail every request or,
 * worse, fall back to process memory and produce the random-logout behaviour this
 * design exists to prevent. Both are worse than refusing to start, so register()
 * rejects and the process exits non-zero.
 *
 * Note: no `experimental.instrumentationHook` is needed. instrumentation.ts is
 * stable since Next 15, and the flag is a no-op on 16.
 */

export async function register(): Promise<void> {
  // ## Why the runtime check is a WARNING, not an early return
  //
  // The first version of this file did:
  //
  //     if (process.env.NEXT_RUNTIME !== 'nodejs') return;
  //
  // and that silently disabled the entire production guard. `NEXT_RUNTIME` is
  // only set once a request is being handled — at boot in a standalone server it
  // is `undefined`, verified against the real build. So the guard read
  // `undefined !== 'nodejs'`, returned, and a production process started happily
  // on a process-local session store. The boot checks all passed while the
  // feature was inert: bundled, exported, and never executed.
  //
  // So: bail only for the runtime that genuinely cannot host the session store,
  // and let a missing NEXT_RUNTIME fall through to the checks.
  const runtime = process.env.NEXT_RUNTIME;
  if (runtime && runtime !== 'nodejs') {
    console.warn(
      `[startup] skipping session store checks: NEXT_RUNTIME=${runtime}`,
    );
    return;
  }

  const {
  assertProductionSessionStore,
  isProductionEnv,
  resolveSessionStoreKind,
} = await import('@/lib/auth/session-store-factory');
  const { getRedisClient } = await import('@/lib/auth/redis-client');

  const kind = resolveSessionStoreKind(process.env);

  // Throws in production on a process-local store. Outside production a
  // misconfiguration is a warning, not a refusal — a developer running without
  // Redis should still be able to work.
  assertProductionSessionStore(kind, process.env);

  if (kind === 'memory') {
    if (isProductionEnv(process.env)) {
      // Unreachable given the guard above; kept so a future change to the guard
      // cannot silently downgrade production to process memory.
      throw new Error('Refusing to start: production cannot use a process-local session store.');
    }
    console.warn(
      '[startup] session store is process memory — correct for one replica only. ' +
        'Set REDIS_URL before running more than one instance.',
    );
    return;
  }

  await getRedisClient();
  console.log('[startup] Redis session store connected');
}
