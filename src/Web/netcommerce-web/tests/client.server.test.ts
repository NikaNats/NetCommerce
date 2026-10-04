import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * The security-critical BFF client. The audit found this module had NO tests at
 * all: mutating it to drop the bearer token, drop the correlation ID, drop the
 * idempotency key, or stop returning null on 401 left the suite green.
 *
 * These tests pin the header contract and the error contract.
 */

const sessionState: { accessToken: string | undefined } = { accessToken: 'access-1' };

vi.mock('@/lib/auth/server-session', () => ({
  getSession: async () =>
    sessionState.accessToken ? { id: 's1', tokens: { current: () => ({ accessToken: sessionState.accessToken }) } } : undefined,
}));

vi.mock('@/lib/config', () => ({
  readConfig: () => ({
    apiBaseUrl: 'http://api',
    keycloakBaseUrl: 'http://keycloak',
    keycloakRealm: 'netcommerce',
    keycloakClientId: 'netcommerce-web',
    callbackPath: '/callback',
  }),
  CALLBACK_PATH: '/callback',
  ConfigError: class extends Error {},
}));

import { ApiError, apiFetch, fetchSessionInfo } from '@/lib/api/client.server';

let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => {
  sessionState.accessToken = 'access-1';
  // A fresh Response per call: a body can only be consumed once, so sharing one
  // instance across calls makes the second read throw.
  fetchMock = vi.fn().mockImplementation(
    async () => new Response(JSON.stringify({ ok: true }), { status: 200 }),
  );
  vi.stubGlobal('fetch', fetchMock);
});

/** Read the init object from the Nth fetch call. */
function callInit(n = 0): RequestInit & { headers: Headers } {
  const init = fetchMock.mock.calls[n]![1] as RequestInit & { headers: Headers };
  return init;
}

describe('apiFetch', () => {
  it('prefixes the configured API base URL', async () => {
    await apiFetch('/api/v1/orders');
    expect(fetchMock.mock.calls[0]![0]).toBe('http://api/api/v1/orders');
  });

  it('attaches the server-held bearer token', async () => {
    await apiFetch('/api/v1/auth/session');
    expect(callInit().headers.get('Authorization')).toBe('Bearer access-1');
  });

  it('omits Authorization when there is no session', async () => {
    sessionState.accessToken = undefined;
    await apiFetch('/api/v1/products');
    expect(callInit().headers.has('Authorization')).toBe(false);
  });

  it('always sends X-Correlation-ID', async () => {
    await apiFetch('/api/v1/products');
    expect(callInit().headers.get('X-Correlation-ID')).toBeTruthy();
  });

  it('generates a distinct correlation id per request', async () => {
    await apiFetch('/api/v1/products');
    await apiFetch('/api/v1/products');
    expect(callInit(0).headers.get('X-Correlation-ID')).not.toBe(
      callInit(1).headers.get('X-Correlation-ID'),
    );
  });

  it('never sends traceparent — the API does not consume it', async () => {
    await apiFetch('/api/v1/products');
    expect(callInit().headers.has('traceparent')).toBe(false);
  });

  it('attaches an idempotency key on POST', async () => {
    await apiFetch('/api/v1/basket/items', { method: 'POST', body: {} });
    expect(callInit().headers.get('X-Idempotency-Key')).toBeTruthy();
  });

  it('reuses a caller-supplied idempotency key so a retry stays safe', async () => {
    const key = 'stable-key';
    await apiFetch('/api/v1/orders', { method: 'POST', body: {}, idempotencyKey: key });
    expect(callInit().headers.get('X-Idempotency-Key')).toBe(key);
  });

  it('does NOT attach an idempotency key on GET', async () => {
    await apiFetch('/api/v1/products');
    expect(callInit().headers.has('X-Idempotency-Key')).toBe(false);
  });

  it('sets Content-Type only when there is a body', async () => {
    await apiFetch('/api/v1/products');
    expect(callInit().headers.has('Content-Type')).toBe(false);

    fetchMock.mockClear();
    await apiFetch('/api/v1/basket/items', { method: 'POST', body: { a: 1 } });
    expect(callInit().headers.get('Content-Type')).toBe('application/json');
  });

  it('serializes the body as JSON', async () => {
    await apiFetch('/api/v1/basket/items', { method: 'POST', body: { productId: 'p1' } });
    expect(JSON.parse(callInit().body as string)).toEqual({ productId: 'p1' });
  });

  it('returns the parsed body on success', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ id: 'o1' }), { status: 200 }));
    expect(await apiFetch('/api/v1/orders')).toEqual({ id: 'o1' });
  });

  it('returns undefined for 204 No Content', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
    expect(await apiFetch('/api/v1/basket', { method: 'DELETE' })).toBeUndefined();
  });

  it('throws ApiError carrying status and correlation id on failure', async () => {
    // Fresh instance per call: the error body is consumed on the first attempt,
    // so a shared Response would leave the retry with nothing to parse.
    fetchMock.mockImplementation(
      async () => new Response(JSON.stringify({ detail: 'nope' }), { status: 422 }),
    );

    await expect(apiFetch('/api/v1/orders', { method: 'POST', body: {} })).rejects.toThrow(
      ApiError,
    );

    try {
      await apiFetch('/api/v1/orders', { method: 'POST', body: {} });
      throw new Error('expected a throw');
    } catch (e) {
      const err = e as ApiError;
      expect(err.status).toBe(422);
      expect(err.correlationId).toBeTruthy();
      expect(err.body).toEqual({ detail: 'nope' });
    }
  });

  it('still throws ApiError when the error body is not JSON', async () => {
    fetchMock.mockResolvedValue(new Response('<html>502</html>', { status: 502 }));
    try {
      await apiFetch('/api/v1/products');
      throw new Error('should have thrown');
    } catch (e) {
      expect(e).toBeInstanceOf(ApiError);
      expect((e as ApiError).status).toBe(502);
      expect((e as ApiError).body).toBeUndefined();
    }
  });

  it('bounds every request with a timeout signal so a hung API cannot hang the page', async () => {
    await apiFetch('/api/v1/products');
    expect(callInit().signal).toBeInstanceOf(AbortSignal);
  });

  it('respects a caller-supplied signal instead of the default timeout', async () => {
    const controller = new AbortController();
    await apiFetch('/api/v1/products', { signal: controller.signal });
    expect(callInit().signal).toBe(controller.signal);
  });
});

describe('fetchSessionInfo', () => {
  it('returns the session payload on success', async () => {
    const info = {
      user_id: 'u1',
      username: 'admin@netcommerce.com',
      email: null,
      realm_roles: ['admin'],
      client_roles: [],
      tenant_id: null,
      token_expires_at: null,
      authenticated_at: null,
      session_state: null,
    };
    fetchMock.mockResolvedValue(new Response(JSON.stringify(info), { status: 200 }));

    expect(await fetchSessionInfo()).toEqual(info);
    expect(fetchMock.mock.calls[0]![0]).toBe('http://api/api/v1/auth/session');
  });

  it('returns null on 401 rather than throwing — an anonymous visitor is not an error', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));
    expect(await fetchSessionInfo()).toBeNull();
  });

  it('propagates a 500 instead of masking it as "signed out"', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 500 }));
    await expect(fetchSessionInfo()).rejects.toThrow(ApiError);
  });
});