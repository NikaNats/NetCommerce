import {
  buildRequestHeaders,
  isMutation,
  newCorrelationId,
  newIdempotencyKey,
} from '@/lib/api/headers';
import { readConfig } from '@/lib/config';
import { getSession } from '@/lib/auth/server-session';

/**
 * Server-side client for the NetCommerce API.
 *
 * Responsibilities, and nothing else:
 *   - attach the server-held bearer token (never a browser-held one)
 *   - attach X-Correlation-ID, the only correlation header the API reads
 *   - attach X-Idempotency-Key on mutations, reusing a caller key on retry
 *
 * It does NOT mint tokens. It does not parse JWTs. It does not invent a
 * traceparent header. Auth failures surface as typed errors so callers can
 * distinguish "log in again" from "the API is down".
 */

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly correlationId: string,
    readonly body?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Supply to make a retry of the same logical operation idempotent. */
  idempotencyKey?: string;
  correlationId?: string;
  /** Next.js fetch cache controls, passed straight through. */
  next?: { revalidate?: number; tags?: string[] };
  cache?: RequestCache;
  /**
   * Abort signal for the outbound request. Defaults to a 15s timeout: without
   * one a hung API hangs the page until Next's own (much longer) limits, and
   * in production that is a thread held per request for no reason.
   */
  signal?: AbortSignal;
}

/**
 * Bound for a single API round trip.
 *
 * Fifteen seconds is generous for catalog reads and order writes alike, and
 * far below any platform timeout — the point is to convert "hung forever"
 * into a catchable failure the pages already know how to render.
 */
export const DEFAULT_API_TIMEOUT_MS = 15_000;

export async function apiFetch<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase();
  const config = readConfig();

  // Does this call participate in Next's Data Cache?
  //
  // Next builds the fetch cache key from the URL, method AND ALL HEADERS
  // (incremental-cache/index.ts). Two of ours are per-request unique, which would
  // make the key unique too:
  //
  //   - X-Correlation-ID, a fresh UUID per call
  //   - Authorization, whenever a session exists — and Next treats an
  //     `authorization` header as making the fetch UNCACHEABLE outright
  //     (patch-fetch.ts: `hasUnCacheableHeader`)
  //
  // So a cached catalog GET with a random correlation id never hits and never
  // dedupes: 0% hit rate, and an orphaned entry written per call into
  // .next/cache/fetch-cache/. In a container that is unbounded disk growth.
  //
  // The catalogue endpoints are PUBLIC (ProductEndpoints has no
  // RequireAuthorization on its GETs), so sending credentials and a unique trace
  // header there is not merely wasteful — it grants nothing and asks for nothing.
  const isCached = Boolean(options.next?.revalidate || options.next?.tags);

  // Trace id: stable per call, so it is never sent on a cacheable request. It is
  // still generated for logs so a non-cached failure can be correlated.
  const correlationId = options.correlationId ?? newCorrelationId();

  const session = isCached ? null : await getSession();

  const headers = buildRequestHeaders({
    method,
    // Omitted on cached calls, deliberately. See above.
    accessToken: session?.tokens.current()?.accessToken,
    idempotencyKey: isMutation(method)
      ? (options.idempotencyKey ?? newIdempotencyKey())
      : undefined,
    // A per-request unique header cannot appear on a cacheable request without
    // destroying the cache key. Cached catalogue reads are anonymous and
    // untraceable per-request by design; anything that NEEDS a trace id must not
    // be cached, which is what `isCached` decides.
    correlationId: isCached ? undefined : correlationId,
  });

  if (options.body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(`${config.apiBaseUrl}${path}`, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    cache: options.cache,
    next: options.next,
    signal: options.signal ?? AbortSignal.timeout(DEFAULT_API_TIMEOUT_MS),
  });

  if (!response.ok) {
    let body: unknown;
    try {
      body = await response.json();
    } catch {
      body = undefined;
    }
    throw new ApiError(
      `API ${method} ${path} failed with ${response.status}`,
      response.status,
      correlationId,
      body,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

/** Identity as reported by the backend's own introspection endpoint. */
export interface SessionInfo {
  user_id: string;
  username: string;
  email: string | null;
  realm_roles: string[];
  client_roles: string[];
  tenant_id: string | null;
  token_expires_at: string | null;
  authenticated_at: string | null;
  session_state: string | null;
}

export async function fetchSessionInfo(): Promise<SessionInfo | null> {
  try {
    return await apiFetch<SessionInfo>('/api/v1/auth/session', { cache: 'no-store' });
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) return null;
    throw cause;
  }
}
