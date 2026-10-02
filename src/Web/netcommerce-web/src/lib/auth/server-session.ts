import { cookies } from 'next/headers';

import { readConfig } from '@/lib/config';
import { createTokenStore, type Session, type TokenStore } from '@/lib/auth/token-store';
import { SESSION_COOKIE, newSessionId, readSessionId } from '@/lib/auth/session-cookie';

/**
 * Server-side session registry.
 *
 * ## Current backing store: process memory
 *
 * The registry below is a module-level Map. That is correct for a single
 * Next.js process in local dev, and it is NOT correct for a multi-instance
 * deployment — a second replica would not see the session and every user would
 * appear logged out at random depending on which replica served them.
 *
 * This is called out in the module docs rather than hidden: the swap point is
 * `registry` below, and it must preserve every semantic documented on
 * TokenStore. Do not "fix" this by letting the browser hold the refresh token.
 *
 * ## Bounded lifetime
 *
 * Sessions expire after SESSION_ABSOLUTE_TTL_MS regardless of activity. A
 * refresh may keep a session *usable*, but never *immortal* — otherwise a
 * stolen cookie would be renewable forever. Expired entries are swept lazily on
 * registry access, which is what stops the Map growing without bound.
 *
 * The tokens themselves never leave the server process. The browser holds only
 * the opaque session id in an httpOnly cookie.
 */

/** Hard ceiling on session lifetime, independent of token refreshes. */
export const SESSION_ABSOLUTE_TTL_MS = 8 * 60 * 60 * 1000;

export interface ServerSession {
  id: string;
  tokens: TokenStore;
  createdAt: number;
}

const registry = new Map<string, ServerSession>();

function store(): TokenStore {
  const config = readConfig();
  return createTokenStore({ baseUrl: config.apiBaseUrl });
}

function isExpired(session: ServerSession, now: number): boolean {
  return now - session.createdAt >= SESSION_ABSOLUTE_TTL_MS;
}

/** Drop every session past its TTL. Runs on registry access, not on a timer. */
function sweep(now: number): void {
  for (const [id, session] of registry) {
    if (isExpired(session, now)) registry.delete(id);
  }
}

export function registerSession(): ServerSession {
  const now = Date.now();
  sweep(now);

  const session: ServerSession = {
    id: newSessionId(),
    tokens: store(),
    createdAt: now,
  };

  registry.set(session.id, session);
  return session;
}

export function getSessionById(id: string | undefined): ServerSession | undefined {
  if (!id) return undefined;

  const now = Date.now();
  sweep(now);

  const session = registry.get(id);
  if (!session) return undefined;

  if (isExpired(session, now)) {
    registry.delete(id);
    return undefined;
  }

  return session;
}

/**
 * Resolve the current session, refreshing through the backend when the access
 * token is close to expiry. Returns undefined when there is no usable session.
 */
export async function getSession(): Promise<ServerSession | undefined> {
  const store = await cookies();
  const id = readSessionId(store.get(SESSION_COOKIE)?.value);
  const session = getSessionById(id);

  if (!session) return undefined;

  // The refresh token's own lifetime bounds the session. Without this check an
  // 8h session could outlive a 30-minute refresh token and sit there "valid"
  // while every actual call would 401.
  const tokens = session.tokens.current();
  if (!tokens || Date.now() >= tokens.refreshExpiresAt) {
    registry.delete(session.id);
    return undefined;
  }

  if (session.tokens.needsRefresh()) {
    const refreshed = await session.tokens.refresh();
    // A failed rotation means the Keycloak session is gone. Drop ours too so
    // the next request is an honest login rather than a loop of 401s.
    if (!refreshed) {
      registry.delete(session.id);
      return undefined;
    }
  }

  if (!session.tokens.current()) {
    registry.delete(session.id);
    return undefined;
  }

  return session;
}

export async function destroySession(): Promise<void> {
  const store = await cookies();
  const id = readSessionId(store.get(SESSION_COOKIE)?.value);
  const session = getSessionById(id);
  if (session) {
    await session.tokens.logout();
    registry.delete(session.id);
  }
  store.delete(SESSION_COOKIE);
}

/**
 * Drop a session by id without touching cookies — for cleanup paths that have
 * already decided the request is unauthenticated (e.g. a failed token exchange,
 * where no cookie was ever set).
 */
export function destroySessionById(id: string): void {
  registry.delete(id);
}

export type { Session };

/* ------------------------------------------------------------------ *
 * Test seams. Not part of the app's public surface — exported only so the
 * suite can observe registry size and isolate cases.
 * ------------------------------------------------------------------ */
export function __registrySize(): number {
  return registry.size;
}

export function __resetRegistryForTests(): void {
  registry.clear();
}
