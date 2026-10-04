import { readConfig } from '@/lib/config';

/**
 * Readiness probe: can this process do useful work?
 *
 * Checks the three things every data page needs — valid configuration, a
 * reachable session store, and a reachable API — each isolated so one failure
 * cannot mask the others, and each bounded so a hung dependency fails the
 * probe instead of hanging it. Returns 200 when ready, 503 when not, so an
 * orchestrator stops routing to a pod that cannot serve.
 *
 * The handler itself never throws: a probe that 500s on an unexpected error
 * looks identical to "not ready" to the orchestrator but hides the cause from
 * operators, so the outer catch reports it explicitly.
 */

export const dynamic = 'force-dynamic';
export const revalidate = 0;

/** A dependency that answers slowly is down for probe purposes. */
const CHECK_TIMEOUT_MS = 3_000;

interface CheckResult {
  status: 'up' | 'down';
  detail?: string;
}

function withTimeout<T>(work: Promise<T>, label: string): Promise<T> {
  let timer: ReturnType<typeof setTimeout>;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error(`${label} timed out`)), CHECK_TIMEOUT_MS);
  });
  return Promise.race([work, timeout]).finally(() => clearTimeout(timer!));
}

async function checkConfig(): Promise<CheckResult> {
  try {
    // Throws ConfigError outside development when a variable is missing or
    // malformed — exactly the misconfiguration that must fail a deploy.
    readConfig();
    return { status: 'up' };
  } catch (cause) {
    return {
      status: 'down',
      detail: cause instanceof Error ? cause.message : 'invalid configuration',
    };
  }
}

async function checkSessionStore(): Promise<CheckResult> {
  try {
    const { resolveSessionStoreKind } = await import(
      '@/lib/auth/session-store-factory'
    );
    const kind = resolveSessionStoreKind(process.env);

    if (kind === 'memory') {
      // Correct for one local process; a multi-replica production pod behind
      // a load balancer would sign users out at random.
      return process.env.NODE_ENV === 'production'
        ? { status: 'down', detail: 'process-local session store in production' }
        : { status: 'up', detail: 'process memory (development only)' };
    }

    const { getRedisClient } = await import('@/lib/auth/redis-client');
    const client = await withTimeout(getRedisClient(), 'redis connect');
    // No PING on the narrow RedisLike surface; a GET for a key that never
    // exists proves the round trip all the same (null arrival, not an error).
    await withTimeout(client.get('nc:health'), 'redis read');
    return { status: 'up', detail: 'redis' };
  } catch (cause) {
    return {
      status: 'down',
      detail: cause instanceof Error ? cause.message : 'session store unreachable',
    };
  }
}

async function checkApi(): Promise<CheckResult> {
  let apiBaseUrl: string;
  try {
    apiBaseUrl = readConfig().apiBaseUrl;
  } catch (cause) {
    return {
      status: 'down',
      detail: cause instanceof Error ? cause.message : 'invalid configuration',
    };
  }

  try {
    const started = Date.now();
    const response = await fetch(`${apiBaseUrl}/health/ready`, {
      signal: AbortSignal.timeout(CHECK_TIMEOUT_MS),
    });
    if (!response.ok) {
      return { status: 'down', detail: `api /health/ready answered ${response.status}` };
    }
    return { status: 'up', detail: `${Date.now() - started}ms` };
  } catch (cause) {
    return {
      status: 'down',
      detail: cause instanceof Error ? cause.message : 'api unreachable',
    };
  }
}

export async function GET(): Promise<Response> {
  try {
    const [config, sessionStore, api] = await Promise.all([
      checkConfig(),
      checkSessionStore(),
      checkApi(),
    ]);

    const ready =
      config.status === 'up' &&
      sessionStore.status === 'up' &&
      api.status === 'up';

    return Response.json(
      { status: ready ? 'ready' : 'not-ready', checks: { config, sessionStore, api } },
      { status: ready ? 200 : 503 },
    );
  } catch (cause) {
    return Response.json(
      {
        status: 'not-ready',
        checks: {},
        detail: cause instanceof Error ? cause.message : 'readiness check failed',
      },
      { status: 503 },
    );
  }
}
