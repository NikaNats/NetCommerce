import Link from 'next/link';

import { ArrowIcon } from '@/components/icons';
import { fetchSessionInfo } from '@/lib/api/client.server';

export const dynamic = 'force-dynamic';

/**
 * Home. Identity is read from the BACKEND's introspection endpoint
 * (/api/v1/auth/session) using the server-held token — never from a
 * browser-readable claim.
 */
export default async function HomePage() {
  let session = null;
  let apiError: string | null = null;

  try {
    session = await fetchSessionInfo();
  } catch (cause) {
    apiError = cause instanceof Error ? cause.message : 'Unknown error';
  }

  return (
    <main id="main" className="stack rail">
      <div>
        <p className="lede">
          Stock is reserved before payment is taken. Every order carries a grace
          period you can cancel inside — no ghost charges.
        </p>
      </div>

      {session ? (
        <section className="panel stack" aria-labelledby="account-heading">
          <h2 id="account-heading" className="heading-minor">
            Signed in
          </h2>

          <p style={{ margin: 0, fontFamily: 'var(--font-display)', fontSize: '1.5rem' }}>
            {session.username}
          </p>

          <dl className="stack" style={{ gap: '0.5rem', margin: 0 }}>
            <div className="row" style={{ justifyContent: 'space-between' }}>
              <dt className="field-label">Subject</dt>
              <dd className="figure" style={{ margin: 0, fontSize: '0.8125rem' }}>
                {session.user_id}
              </dd>
            </div>

            {session.realm_roles.length > 0 ? (
              <div className="row" style={{ justifyContent: 'space-between' }}>
                <dt className="field-label">Roles</dt>
                <dd className="figure" style={{ margin: 0, fontSize: '0.8125rem' }}>
                  {session.realm_roles.join(', ')}
                </dd>
              </div>
            ) : null}
          </dl>

          <form action="/api/auth/logout" method="post">
            <button className="btn btn--ghost" type="submit">
              Sign out
            </button>
          </form>
        </section>
      ) : (
        <section className="panel stack" aria-labelledby="signin-heading">
          <h2 id="signin-heading" className="heading-minor">
            Not signed in
          </h2>
          <p style={{ margin: 0 }}>
            Sign-in is handled by Keycloak. This app never sees your password, and
            your browser never holds a token.
          </p>
          <div>
            <Link className="btn" href="/login">
              Sign in <ArrowIcon />
            </Link>
          </div>
        </section>
      )}

      {apiError ? (
        <section className="notice" role="status">
          <strong>API unreachable</strong>
          <p style={{ margin: 0 }}>
            <span className="figure">{apiError}</span> — is the API running under
            the Aspire AppHost, with <span className="figure">API_BASE_URL</span>{' '}
            set?
          </p>
        </section>
      ) : null}
    </main>
  );
}