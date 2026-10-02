import { redirect } from 'next/navigation';
import type { Route } from 'next';

import { getSession, type ServerSession } from '@/lib/auth/server-session';
import { fetchSessionInfo } from '@/lib/api/client.server';

/**
 * Route guards for Server Components.
 *
 * ## Use these, not ad-hoc checks
 *
 * A page that forgets to check its session renders anyway and leaks its data
 * shape (or empty-state UI implying "no orders" to a signed-out visitor). These
 * helpers make "this route requires a session" a one-line, testable declaration.
 *
 * The API enforces authorization independently — `RequireAuthorization()` and
 * `RequireRateLimiting("PerUser")` on the endpoints, and the subject comes from
 * the validated token. These guards are for UX and defence in depth, never the
 * only thing between a user and someone else's data.
 *
 * ## Roles come from the BACKEND
 *
 * Role checks call the API's own /auth/session introspection. They never decode
 * a JWT locally: an unverified payload is attacker-controlled input, and using it
 * for an authorization decision would make the frontend the security boundary.
 */

/** Thrown by Next when a redirect occurs; tests assert on it. */
export class GuardRedirect extends Error {
  constructor(
    readonly location: string,
    readonly reason: 'anonymous' | 'forbidden',
  ) {
    super(`NEXT_REDIRECT;${reason};${location}`);
    this.name = 'GuardRedirect';
  }
}

/**
 * Require an authenticated session, or redirect to /login.
 *
 * `returnTo` is preserved so the user lands back where they intended instead of
 * at the homepage.
 */
export async function requireSession(returnTo = '/'): Promise<ServerSession> {
  const session = await getSession();

  if (!session) {
    const target = `/login?returnTo=${encodeURIComponent(returnTo)}`;
    redirect(target as Route);
    // redirect() throws; this is unreachable and only satisfies the type checker.
    throw new GuardRedirect(target, 'anonymous');
  }

  return session;
}

/**
 * Require one of the given roles, or deny.
 *
 * An anonymous visitor is sent to /login by requireSession — that is correct and
 * carries no disclosure, because it happens for EVERY protected route whether or
 * not the route exists. What must not happen is an anonymous visitor being told
 * "you are authenticated but lack the role", which would reveal that an admin
 * area exists at all. Both paths below therefore avoid leaking WHICH check failed.
 */
export async function requireRole(
  returnTo = '/',
  allowed: string | readonly string[],
): Promise<void> {
  // Anonymous users go to login; an authenticated user who lacks the role is
  // denied below. Neither response distinguishes "no such route" from
  // "route exists, not permitted".
  await requireSession(returnTo);

  const info = await fetchSessionInfo();
  const roles = [...(info?.realm_roles ?? []), ...(info?.client_roles ?? [])];
  const permitted = typeof allowed === 'string' ? [allowed] : allowed;

  if (!roles.some((role) => permitted.includes(role))) {
    throw new GuardRedirect('/', 'forbidden');
  }
}