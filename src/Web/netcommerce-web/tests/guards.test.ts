import { beforeEach, describe, expect, it, vi } from 'vitest';

const cookieJar = new Map<string, string>();

vi.mock('next/headers', () => ({
  cookies: async () => ({
    get: (name: string) =>
      cookieJar.has(name) ? { name, value: cookieJar.get(name)! } : undefined,
    set: (n: string, v: string) => cookieJar.set(n, v),
    delete: (n: string) => cookieJar.delete(n),
  }),
}));

vi.mock('@/lib/config', () => ({
  readConfig: () => ({
    apiBaseUrl: 'http://api',
    keycloakBaseUrl: 'http://keycloak',
    keycloakRealm: 'netcommerce',
    keycloakClientId: 'netcommerce-web',
    callbackPath: '/callback',
    publicOrigin: null,
  }),
  CALLBACK_PATH: '/callback',
  publicOrigin: (u: string) => new URL(u).origin,
  ConfigError: class extends Error {},
}));

import { getSession } from '@/lib/auth/server-session';
import { fetchSessionInfo } from '@/lib/api/client.server';
import { requireSession, requireRole } from '@/lib/auth/guards';

vi.mock('@/lib/auth/server-session', async () => {
  const actual = await vi.importActual<typeof import('@/lib/auth/server-session')>(
    '@/lib/auth/server-session',
  );
  return {
    ...actual,
    getSession: vi.fn(),
  };
});

// fetchSessionInfo reaches the API; stub it so role checks are unit-testable.
vi.mock('@/lib/api/client.server', () => ({
  fetchSessionInfo: vi.fn(),
}));

const getSessionMock = vi.mocked(getSession);
const fetchSessionInfoMock = vi.mocked(fetchSessionInfo);

beforeEach(() => {
  cookieJar.clear();
  getSessionMock.mockReset();
  getSessionMock.mockResolvedValue(undefined);
  fetchSessionInfoMock.mockReset();
  fetchSessionInfoMock.mockResolvedValue(null);
});

const fakeSession = (roles: string[] = ['customer']) => ({
  id: 's1',
  tokens: { current: () => ({ accessToken: 'a1' }) },
  roles,
});

/**
 * Next's redirect() throws an opaque error whose digest carries the location.
 * Read the location out of whatever was thrown rather than assuming a shape.
 */
async function redirectLocation(promise: Promise<unknown>): Promise<string> {
  try {
    await promise;
    return '<resolved — no redirect happened>';
  } catch (e) {
    const err = e as { digest?: string; message?: string };
    return String(err?.digest ?? err?.message ?? err);
  }
}

describe('requireSession', () => {
  it('returns the session when one exists', async () => {
    const session = fakeSession();
    getSessionMock.mockResolvedValue(session as never);

    await expect(requireSession('/orders/123')).resolves.toBe(session);
  });

  it('redirects an anonymous visitor to login', async () => {
    expect(await redirectLocation(requireSession('/orders/123'))).toContain('/login');
  });

  it('preserves the intended destination so login can send the user back', async () => {
    const location = await redirectLocation(requireSession('/orders/abc'));
    expect(location).toContain(encodeURIComponent('/orders/abc'));
  });

  it('does NOT redirect when a session exists', async () => {
    getSessionMock.mockResolvedValue(fakeSession() as never);
    await expect(requireSession('/orders/123')).resolves.toBeDefined();
  });
});

describe('requireRole', () => {
  /** Roles come from the BACKEND's introspection, never a local JWT decode. */
  function backendSession(realmRoles: string[], clientRoles: string[] = []) {
    fetchSessionInfoMock.mockResolvedValue({
      user_id: 'u1',
      username: 'admin@netcommerce.com',
      email: null,
      realm_roles: realmRoles,
      client_roles: clientRoles,
      tenant_id: null,
      token_expires_at: null,
      authenticated_at: null,
      session_state: null,
    });
  }

  it('passes when the backend reports the role', async () => {
    getSessionMock.mockResolvedValue(fakeSession() as never);
    backendSession(['admin']);
    await expect(requireRole('/admin', 'admin')).resolves.toBeUndefined();
  });

  it('denies when the backend does not report the role', async () => {
    getSessionMock.mockResolvedValue(fakeSession() as never);
    backendSession(['customer']);

    const denied = await redirectLocation(requireRole('/admin', 'admin'));
    // Must NOT be a login redirect — the user IS authenticated, just not allowed.
    expect(denied).not.toContain('/login');
  });

  it('sends an anonymous visitor to login, exactly like any other protected route', async () => {
    getSessionMock.mockResolvedValue(undefined);

    // requireRole delegates to requireSession, so an anonymous visitor lands on
    // /login. That is not a disclosure: EVERY protected route does this,
    // whether or not it exists, so it reveals nothing about admin areas.
    expect(await redirectLocation(requireRole('/admin', 'admin'))).toContain('/login');
  });

  it('accepts any of several allowed roles', async () => {
    getSessionMock.mockResolvedValue(fakeSession() as never);
    backendSession(['operator']);
    await expect(requireRole('/admin', ['admin', 'operator'])).resolves.toBeUndefined();
  });

  it('honours client roles as well as realm roles', async () => {
    getSessionMock.mockResolvedValue(fakeSession() as never);
    backendSession([], ['inventory-admin']);
    await expect(requireRole('/admin', 'inventory-admin')).resolves.toBeUndefined();
  });

  it('denies when the backend cannot confirm the session', async () => {
    getSessionMock.mockResolvedValue(fakeSession() as never);
    fetchSessionInfoMock.mockResolvedValue(null);
    expect(await redirectLocation(requireRole('/admin', 'admin'))).not.toContain('/login');
  });
});