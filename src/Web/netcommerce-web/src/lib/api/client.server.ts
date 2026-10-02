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
}

export async function apiFetch<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase();
  const correlationId = options.correlationId ?? newCorrelationId();
  const config = readConfig();

  const session = await getSession();
  const headers = buildRequestHeaders({
    method,
    accessToken: session?.tokens.current()?.accessToken,
    idempotencyKey: isMutation(method)
      ? (options.idempotencyKey ?? newIdempotencyKey())
      : undefined,
    correlationId,
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
