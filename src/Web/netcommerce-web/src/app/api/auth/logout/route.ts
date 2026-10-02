import { redirect } from 'next/navigation';

import { destroySession } from '@/lib/auth/server-session';

/**
 * Sign out: revoke the refresh token at Keycloak via the backend, then drop
 * the local session. POST-only so a stray GET (or an <img> tag) cannot log a
 * user out.
 */
export async function POST(): Promise<never> {
  await destroySession();
  redirect('/');
}
