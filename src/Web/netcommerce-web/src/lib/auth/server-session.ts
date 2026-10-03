import { cookies } from 'next/headers';

import { readConfig } from '@/lib/config';
import { createTokenStore, type Session, type TokenStore } from '@/lib/auth/token-store';
import { SESSION_COOKIE, newSessionId, readSessionId } from '@/lib/auth/session-cookie';
import { getSessionStore } from '@/lib/auth/session-store-factory';
import { SESSION_ABSOLUTE_TTL_MS } from '@/lib/auth/session-store-ttl';
import type { SessionStore, StoredSession } from '@/lib/auth/session-store';

/**
 * Server-side session registry.
 *
 * ## Backing store
 *
 * Delegates to a `SessionStore` (see session-store.ts). In development that is
 * process memory, which is correct for one process. In production it must be the
 * Redis implementation, and the factory refuses to build a process-local store
 * there — because a per-process store behind a load balancer signs users out at
 * random and never throws.
 *
 * ## Refresh is locked ACROSS processes
 *
 * Keycloak refresh tokens here are single-use (`revokeRefreshToken=true`,
 * `max.reuse=0`). Presenting one twice revokes the ENTIRE session family: every
 * device, not just this one. `TokenStore` already single-flights within a process,
 * which is worthless across replicas — two of them refreshing the same session a
 * second apart would replay one token and sign the user out everywhere.
 *
 * So rotation happens inside `store.withRefreshLock`, exclusive across processes.
 * A caller that loses the lock does NOT attempt a rotation; it re-reads the
 * stored session, which now carries the winner's rotated pair.
 *
 * ## Bounded lifetime
 *
 * Sessions expire after SESSION_ABSOLUTE_TTL_MS regardless of activity. A refresh
 * may keep a session usable, never immortal — otherwise a stolen cookie would be
 * renewable forever. The Redis TTL enforces the same bound independently, so a
 * crashed process leaves nothing behind and no sweeper is required.
 *
 * The tokens themselves never leave the server. The browser holds only the opaque
 * session id in an httpOnly cookie.
 */

export { SESSION_ABSOLUTE_TTL_MS };

export interface ServerSession {
  id: string;
  tokens: TokenStore;
  createdAt: number;
}

/**
 * How long a lock loser waits for the winner's rotated pair to land.
 *
 * Must comfortably exceed a normal token round trip but stay far below the
 * request timeout, so a genuinely stuck winner fails fast instead of hanging the
 * request.
 */
const ROTATION_WAIT_MS = 5_000;

function sessionStore(): SessionStore {
  return getSessionStore();
}

function isExpired(stored: StoredSession, now: number): boolean {
  return now - stored.createdAt >= SESSION_ABSOLUTE_TTL_MS;
}

/**
 * Rehydrate a stored session into a live TokenStore.
 *
 * Rebuilt per request rather than cached: it is a thin closure over the token
 * pair, and a fresh one guarantees it reads persisted state rather than a value
 * captured before a concurrent rotation.
 */
function hydrate(stored: StoredSession): ServerSession {
  const tokens = createTokenStore({ baseUrl: readConfig().apiBaseUrl });
  const now = Date.now();

  // A registered-but-not-yet-exchanged session holds placeholder tokens. Feeding
  // those to adopt() throws "contained no refresh token", which would turn a
  // harmless half-created session into a 500 on any request that touched it.
  // Return a store with no session instead: callers already treat a null
  // current() as "no usable session" and fall through to a clean login.
  if (!stored.tokens.refreshToken) {
    return { id: stored.id, tokens, createdAt: stored.createdAt };
  }

  tokens.adoptForTest?.({
    access_token: stored.tokens.accessToken,
    refresh_token: stored.tokens.refreshToken,
    expires_in: Math.max(0, Math.floor((stored.tokens.expiresAt - now) / 1000)),
    refresh_expires_in: Math.max(
      0,
      Math.floor((stored.tokens.refreshExpiresAt - now) / 1000),
    ),
    token_type: 'Bearer',
  });

  return { id: stored.id, tokens, createdAt: stored.createdAt };
}

export async function registerSession(): Promise<ServerSession> {
  const now = Date.now();
  const id = newSessionId();
  const tokens = createTokenStore({ baseUrl: readConfig().apiBaseUrl });

  // No tokens yet: current() is null until the code exchange runs. The callback
  // route calls persistSession() immediately after adopting the real pair, and
  // sets the cookie only after that succeeds — so a session is never readable
  // from this placeholder.
  await sessionStore().put({
    id,
    createdAt: now,
    tokens: { accessToken: '', refreshToken: '', expiresAt: now, refreshExpiresAt: now },
    presentedRefreshTokens: [],
  });

  return { id, tokens, createdAt: now };
}

/**
 * Persist the current token pair for a session.
 *
 * Called after every successful exchange or rotation. Keeping the write in one
 * place means no caller can rotate without recording the new tokens, which is
 * what lets a replica that never saw the rotation keep the session alive.
 */
export async function persistSession(session: ServerSession): Promise<void> {
  const current = session.tokens.current();
  if (!current) return;

  const existing = await sessionStore().get(session.id);
  await sessionStore().put({
    id: session.id,
    createdAt: session.createdAt,
    tokens: current,
    presentedRefreshTokens: existing?.presentedRefreshTokens ?? [],
  });
}

export async function getSessionById(
  id: string | undefined,
): Promise<ServerSession | undefined> {
  if (!id) return undefined;

  const store = sessionStore();
  const stored = await store.get(id);
  if (!stored) return undefined;

  if (isExpired(stored, Date.now())) {
    await store.delete(id);
    return undefined;
  }

  return hydrate(stored);
}

/**
 * Poll for the winner's rotated pair instead of reading once.
 *
 * A caller that loses the lock re-reads immediately, but the winner has NOT
 * written yet — so a single read returns the OLD, already-expiring pair.
 * Returning that gave the caller an access token inside the refresh window, which
 * the API then rejects with a 401 for a user who is genuinely signed in.
 *
 * So: re-read until the refresh token CHANGES, bounded by the same window a
 * rotation can reasonably take. Returning the original record on timeout is
 * correct — a still-valid refresh token beats a fabricated failure.
 */
async function waitForRotation(
  store: SessionStore,
  id: string,
  original: StoredSession,
  now: () => number = () => Date.now(),
  sleep: (ms: number) => Promise<void> = (ms) =>
    new Promise((resolve) => setTimeout(resolve, ms)),
): Promise<StoredSession | null> {
  const deadline = now() + ROTATION_WAIT_MS;
  const intervalMs = 25;

  for (;;) {
    const current = await store.get(id);
    if (!current) return null;

    // Someone wrote a NEW pair — that is the winner's rotation landing.
    if (current.tokens.refreshToken !== original.tokens.refreshToken) {
      return current;
    }

    if (now() >= deadline) {
      // Timed out. The original pair is still valid; better to hand it back
      // than to fail a request the user is entitled to.
      return current;
    }

    await sleep(intervalMs);
  }
}

/**
 * Resolve the current session, refreshing through the backend when the access
 * token is close to expiry. Returns undefined when there is no usable session.
 */
export async function getSession(): Promise<ServerSession | undefined> {
  const cookieStore = await cookies();
  const id = readSessionId(cookieStore.get(SESSION_COOKIE)?.value);
  if (!id) return undefined;

  const store = sessionStore();
  const stored = await store.get(id);
  if (!stored) return undefined;

  if (isExpired(stored, Date.now())) {
    await store.delete(id);
    return undefined;
  }

  const session = hydrate(stored);

  // The refresh token's own lifetime bounds the session. Without this check an
  // 8h session could outlive a 30-minute refresh token and sit there "valid"
  // while every actual call would 401.
  const tokens = session.tokens.current();
  if (!tokens?.refreshToken || Date.now() >= tokens.refreshExpiresAt) {
    await store.delete(id);
    return undefined;
  }

  if (!session.tokens.needsRefresh()) {
    return session;
  }

  // Rotation under a cross-process lock. Everyone who loses the race re-reads
  // rather than rotating, because a second rotation would replay a single-use
  // token and revoke the whole Keycloak session family.
  const outcome = await store.withRefreshLock<StoredSession | null>(id, async () => {
    const spent = tokens.refreshToken;

    // Re-read inside the lock: another replica may have rotated between our read
    // and our acquisition, in which case our token is already spent.
    const fresh = await store.get(id);
    if (!fresh) return null;

    if (fresh.tokens.refreshToken !== spent) {
      // Someone else rotated while we waited. Adopt their result.
      return fresh;
    }

    // Read defensively: a record written by an older version may lack the array,
    // and the Redis store's normalisation is a second line of defence rather than
    // a guarantee for every store implementation.
    const presented = Array.isArray(fresh.presentedRefreshTokens)
      ? fresh.presentedRefreshTokens
      : [];

    if (presented.includes(spent)) {
      // Already presented. Refuse to replay; keep the stored state.
      return fresh;
    }

    const refreshed = await session.tokens.refresh();

    if (!refreshed) {
      // A null refresh result is AMBIGUOUS. Only a TERMINAL rejection means the
      // token is dead; a 429 from the AuthStrict limiter, a 502, or a dropped
      // socket means the token was never presented and is STILL VALID.
      //
      // Deleting the session on either is what signed out every user during a
      // traffic spike. AuthStrict makes a 429 the EXPECTED outcome of a login
      // burst, so the transient case is routine, not exceptional.
      const outcome = session.tokens.lastRefreshOutcome?.() ?? 'transient';

      if (outcome === 'terminal') {
        // Drop ours so the next request is an honest login rather than a loop
        // of 401s.
        await store.delete(id);
        return null;
      }

      // Transient: keep the session and its stored pair. The next request will
      // retry, and the token is still good.
      return fresh;
    }

    const current = session.tokens.current();
    if (!current) return null;

    const updated: StoredSession = {
      ...fresh,
      tokens: current,
      presentedRefreshTokens: [...presented, spent],
    };
    await store.put(updated);
    return updated;
  });

  if (!outcome.acquired) {
    // Lock lost: another replica is rotating right now. Re-read for its result
    // rather than starting a competing rotation.
    const reloaded = await waitForRotation(store, id, stored);
    if (!reloaded) return undefined;

    const reloadedTokens = reloaded.tokens;
    if (!reloadedTokens.refreshToken || Date.now() >= reloadedTokens.refreshExpiresAt) {
      await store.delete(id);
      return undefined;
    }
    return hydrate(reloaded);
  }

  if (!outcome.value) return undefined;

  const finalTokens = outcome.value.tokens;
  if (!finalTokens.refreshToken || Date.now() >= finalTokens.refreshExpiresAt) {
    await store.delete(id);
    return undefined;
  }

  return hydrate(outcome.value);
}

export async function destroySession(): Promise<void> {
  const cookieStore = await cookies();
  const id = readSessionId(cookieStore.get(SESSION_COOKIE)?.value);
  const store = sessionStore();

  if (id) {
    const stored = await store.get(id);
    if (stored?.tokens.refreshToken) {
      // End the Keycloak SSO session, not just our record.
      await hydrate(stored).tokens.logout();
    }
    await store.delete(id);
  }

  cookieStore.delete(SESSION_COOKIE);
}

/**
 * Drop a session by id without touching cookies — for cleanup paths that have
 * already decided the request is unauthenticated (e.g. a failed token exchange,
 * where no cookie was ever set).
 */
export async function destroySessionById(id: string): Promise<void> {
  await sessionStore().delete(id);
}

export type { Session };

/* ------------------------------------------------------------------ *
 * Test seams. Not part of the app's public surface — exported only so the
 * suite can observe store size and isolate cases.
 * ------------------------------------------------------------------ */
export async function __registrySize(): Promise<number> {
  const store = sessionStore() as SessionStore & { __size?: () => number };
  return store.__size?.() ?? 0;
}

export async function __resetRegistryForTests(): Promise<void> {
  await sessionStore().close();
}