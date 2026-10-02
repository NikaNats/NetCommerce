import { beforeEach, describe, expect, it, vi } from 'vitest';

import { buildRequestHeaders, newIdempotencyKey, newCorrelationId } from '@/lib/api/headers';

describe('buildRequestHeaders', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('attaches the bearer token when a session is present', () => {
    const headers = buildRequestHeaders({
      method: 'GET',
      accessToken: 'token-abc',
      idempotencyKey: undefined,
      correlationId: 'corr-1',
    });

    expect(headers.get('Authorization')).toBe('Bearer token-abc');
  });

  it('omits Authorization entirely when there is no session', () => {
    const headers = buildRequestHeaders({
      method: 'GET',
      accessToken: undefined,
      idempotencyKey: undefined,
      correlationId: 'corr-1',
    });

    expect(headers.has('Authorization')).toBe(false);
  });

  it('always sets X-Correlation-ID — the only correlation header the API reads', () => {
    const headers = buildRequestHeaders({
      method: 'GET',
      accessToken: undefined,
      idempotencyKey: undefined,
      correlationId: 'corr-42',
    });

    expect(headers.get('X-Correlation-ID')).toBe('corr-42');
  });

  it('does NOT set traceparent — the API has zero traceparent handling', () => {
    const headers = buildRequestHeaders({
      method: 'GET',
      accessToken: undefined,
      idempotencyKey: undefined,
      correlationId: 'corr-1',
    });

    // CorrelationIdMiddleware.cs:12 reads only X-Correlation-ID.
    // A traceparent header here would be inert decoration.
    expect(headers.has('traceparent')).toBe(false);
  });

  it.each(['POST', 'PUT', 'PATCH', 'DELETE'])('attaches X-Idempotency-Key on %s', (method) => {
    const headers = buildRequestHeaders({
      method,
      accessToken: undefined,
      idempotencyKey: 'idem-1',
      correlationId: 'corr-1',
    });

    expect(headers.get('X-Idempotency-Key')).toBe('idem-1');
  });

  it('does NOT attach X-Idempotency-Key on GET or HEAD', () => {
    for (const method of ['GET', 'HEAD']) {
      const headers = buildRequestHeaders({
        method,
        accessToken: undefined,
        idempotencyKey: 'idem-1',
        correlationId: 'corr-1',
      });

      expect(headers.has('X-Idempotency-Key')).toBe(false);
    }
  });

  it('honors a caller-supplied idempotency key so retries reuse the same key', () => {
    const supplied = 'stable-key-for-retry';
    const first = buildRequestHeaders({
      method: 'POST',
      accessToken: undefined,
      idempotencyKey: supplied,
      correlationId: 'c',
    });
    const retry = buildRequestHeaders({
      method: 'POST',
      accessToken: undefined,
      idempotencyKey: supplied,
      correlationId: 'c',
    });

    expect(first.get('X-Idempotency-Key')).toBe(retry.get('X-Idempotency-Key'));
    expect(first.get('X-Idempotency-Key')).toBe(supplied);
  });

  it('is case-insensitive about the request method', () => {
    const headers = buildRequestHeaders({
      method: 'post',
      accessToken: undefined,
      idempotencyKey: 'idem-1',
      correlationId: 'c',
    });

    expect(headers.get('X-Idempotency-Key')).toBe('idem-1');
  });
});

describe('newIdempotencyKey', () => {
  it('produces a distinct key on every call', () => {
    const keys = new Set(Array.from({ length: 50 }, () => newIdempotencyKey()));
    expect(keys.size).toBe(50);
  });

  it('produces a UUIDv4-shaped value', () => {
    expect(newIdempotencyKey()).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
  });
});

describe('newCorrelationId', () => {
  it('produces a distinct id on every call', () => {
    const ids = new Set(Array.from({ length: 50 }, () => newCorrelationId()));
    expect(ids.size).toBe(50);
  });
});
