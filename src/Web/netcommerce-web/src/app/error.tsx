'use client';

import { useEffect } from 'react';

/**
 * Route-level error boundary.
 *
 * ## Copy that is true whatever the cause
 *
 * In a production build React strips a server-thrown error as it crosses the
 * server/client boundary: what arrives here is a plain `Error` whose only own
 * property is `digest`, and `error.message` is React's generic text. So this
 * deliberately does NOT classify by `error.name`, `instanceof`, or message —
 * anything keyed on those is always true in dev and permanently false in prod.
 *
 * The `digest` IS reliable across that boundary (it is the server-side hash), so
 * it is surfaced verbatim as a support reference.
 */
export default function ErrorBoundary({
  error,
  retry,
}: {
  error: Error & { digest?: string };
  retry: () => void;
}) {
  useEffect(() => {
    // Server-side logging already happened where the error was thrown; this is
    // the browser-side breadcrumb tying it to the render that failed.
    console.error('[render] route error', error);
  }, [error]);

  return (
    <main id="main" className="stack rail">
      <section className="panel stack" role="alert" aria-labelledby="err-heading">
        <h1 id="err-heading" className="heading-major">
          Something went wrong
        </h1>
        <p style={{ margin: 0 }}>
          This page could not be displayed. The problem has been logged. Trying
          again often works; if it does not, the reference below identifies the
          failure for support.
        </p>

        {error.digest ? (
          <p className="figure" style={{ margin: 0 }}>
            Reference: <span className="figure">{error.digest}</span>
          </p>
        ) : null}

        <div className="row">
          <button className="btn" type="button" onClick={() => retry()}>
            Try again
          </button>
          <a className="btn btn--ghost" href="/">
            Go to the storefront
          </a>
        </div>
      </section>
    </main>
  );
}