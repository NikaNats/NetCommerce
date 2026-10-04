import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * The BFF proxy behind `useOrderSaga.reconcile()`.
 *
 * The order page's source of truth is this route, not the socket: it runs on
 * load, on every reconnect, and every 15s while a saga is live. These tests
 * pin the user-observable contract —
 *   - signed out            → 401 with a sign-in message (not a redirect loop),
 *   - someone else's order  → 403 passed through (never reinterpreted as 404),
 *   - missing order         → 404 passed through,
 *   - API down              → 502 (an outage, not a silent wrong state),
 * and that the order id cannot break the upstream routing.
 */

const sessionState: { signedIn: boolean } = { signedIn: true };

vi.mock('@/lib/auth/server-session', () => ({
  getSession: async () => (sessionState.signedIn ? { id: 's1' } : undefined),
}));

const apiState: { impl: (path: string) => Promise<unknown> } = {
  impl: async () => ({ id: 'o1', orderNumber: 'NC-1', status: 3, createdAt: '2026-01-01T00:00:00Z' }),
};

vi.mock('@/lib/api/client.server', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/lib/api/client.server')>();
  return {
    ...original,
    apiFetch: (path: string) => apiState.impl(path),
  };
});

import { GET } from '../src/app/api/bff/orders/[id]/route';
import { ApiError } from '@/lib/api/client.server';

function context(id: string) {
  return { params: Promise.resolve({ id }) };
}

beforeEach(() => {
  sessionState.signedIn = true;
  apiState.impl = async () => ({
    id: 'o1',
    orderNumber: 'NC-1',
    status: 3,
    createdAt: '2026-01-01T00:00:00Z',
  });
});

describe('GET /api/bff/orders/[id]', () => {
  it('returns 401 with a sign-in message when there is no session', async () => {
    sessionState.signedIn = false;

    const res = await GET(new Request('http://x/api/bff/orders/o1'), context('o1'));

    expect(res.status).toBe(401);
    expect(await res.json()).toEqual({ error: 'Sign in to view this order.' });
  });

  it('returns the API projection untouched when signed in', async () => {
    const res = await GET(new Request('http://x/api/bff/orders/o1'), context('o1'));

    expect(res.status).toBe(200);
    const body = (await res.json()) as { status: number; orderNumber: string };
    expect(body.orderNumber).toBe('NC-1');
    // The persisted enum value, for mapOrderStatus — never a saga push string.
    expect(body.status).toBe(3);
  });

  it('passes a 403 through instead of reinterpreting it', async () => {
    apiState.impl = async () => {
      throw new ApiError('Forbidden', 403, 'corr-1');
    };

    const res = await GET(new Request('http://x/api/bff/orders/other'), context('other'));

    expect(res.status).toBe(403);
    expect(await res.json()).toEqual({ error: 'Forbidden', correlationId: 'corr-1' });
  });

  it('passes a 404 through so a missing order reads as missing', async () => {
    apiState.impl = async () => {
      throw new ApiError('Not found', 404, 'corr-2');
    };

    const res = await GET(new Request('http://x/api/bff/orders/nope'), context('nope'));

    expect(res.status).toBe(404);
    expect(await res.json()).toEqual({ error: 'Not found', correlationId: 'corr-2' });
  });

  it('returns 502 when the failure is not an API response', async () => {
    apiState.impl = async () => {
      throw new Error('socket hang up');
    };

    const res = await GET(new Request('http://x/api/bff/orders/o1'), context('o1'));

    expect(res.status).toBe(502);
    expect(await res.json()).toEqual({ error: 'Could not reach the order service.' });
  });

  it('URL-encodes the order id into the upstream path', async () => {
    let seen = '';
    apiState.impl = async (path: string) => {
      seen = path;
      return { id: 'o1', orderNumber: 'NC-1', status: 0, createdAt: '2026-01-01T00:00:00Z' };
    };

    const res = await GET(
      new Request('http://x/api/bff/orders/a%2Fb'),
      context('a/b'),
    );

    expect(res.status).toBe(200);
    expect(seen).toBe('/api/v1/orders/a%2Fb');
  });
});
