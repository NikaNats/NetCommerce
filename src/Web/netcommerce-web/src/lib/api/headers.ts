/**
 * Outbound request headers for calls to the NetCommerce API.
 *
 * Header names are not stylistic choices — they are the API's contract:
 *   X-Idempotency-Key  src/Api/Endpoints/Common/IdempotencyFilter.cs:14
 *   X-Correlation-ID   src/Api/Middleware/CorrelationIdMiddleware.cs:12
 *
 * There is deliberately NO traceparent header. CorrelationIdMiddleware reads
 * only X-Correlation-ID, and a repo-wide search for 'traceparent' in src/
 * returns zero hits, so a W3C header would be inert decoration that looks like
 * distributed tracing while correlating nothing. If the API later adopts W3C
 * propagation, add it here alongside a real consumer — not before.
 */

const IDEMPOTENT_SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

/** HTTP methods whose body is a non-idempotent mutation. */
export function isMutation(method: string): boolean {
  return !IDEMPOTENT_SAFE_METHODS.has(method.toUpperCase());
}

export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}

export function newCorrelationId(): string {
  return crypto.randomUUID();
}

export interface HeaderInput {
  method: string;
  accessToken: string | undefined;
  /** Reuse a stable key across retries of the same logical operation. */
  idempotencyKey: string | undefined;
  // Optional: pass undefined to OMIT the header entirely.
//
// This is not cosmetic. Next includes every header in the fetch cache key
// (incremental-cache/index.ts), so a fresh X-Correlation-ID per request makes every
// cached GET a unique key — 0% hit rate, plus an orphaned entry written per call
// into .next/cache/fetch-cache/. Any call that opts into `next.revalidate`/`next.tags`
// must therefore omit it. See apiFetch in client.server.ts.
correlationId?: string;
}

export function buildRequestHeaders(input: HeaderInput): Headers {
  const headers = new Headers();

  if (input.accessToken) {
    headers.set('Authorization', `Bearer ${input.accessToken}`);
  }

  // Set ONLY when supplied. `Headers.set` with undefined would stringify it to the
  // literal "undefined", which is worse than absent: the API would log a bogus trace
  // id and, more importantly, the header would still be present in the cache key.
  if (input.correlationId) {
    headers.set('X-Correlation-ID', input.correlationId);
  }

  if (isMutation(input.method) && input.idempotencyKey) {
    headers.set('X-Idempotency-Key', input.idempotencyKey);
  }

  return headers;
}
