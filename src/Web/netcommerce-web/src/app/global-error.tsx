'use client';

import { useEffect } from 'react';

/**
 * Root error boundary.
 *
 * Replaces the entire document when it renders, so it MUST define its own
 * `<html>` and `<body>` — confirmed against the installed Next 16.3.8 docs
 * (01-getting-started/10-error-handling.md), which also use `retry` rather than
 * `reset`.
 *
 * Styled inline because the root stylesheet is reached through the layout this
 * boundary has just replaced, so a class-based rule may not apply.
 */
export default function GlobalError({
  error,
  retry,
}: {
  error: Error & { digest?: string };
  retry: () => void;
}) {
  useEffect(() => {
    console.error('[render] root error', error);
  }, [error]);

  return (
    <html lang="en">
      <body
        style={{
          margin: 0,
          minHeight: '100vh',
          display: 'grid',
          placeItems: 'center',
          background: '#f4f1ea',
          color: '#14110f',
          fontFamily: "'Archivo', 'Helvetica Neue', sans-serif",
        }}
      >
        <main style={{ maxWidth: '34rem', padding: '2rem' }}>
          <h1 style={{ fontFamily: "'Bodoni Moda', Didot, Georgia, serif", fontSize: '2rem', margin: '0 0 1rem' }}>
            The application failed to start
          </h1>
          <p style={{ lineHeight: 1.6, margin: '0 0 1rem' }}>
            This is a fault on our side, not something you did. The error has been
            logged with the reference below.
          </p>

          {error.digest ? (
            <p
              style={{
                fontFamily: "'IBM Plex Mono', monospace",
                fontSize: '0.8125rem',
                margin: '0 0 1.5rem',
              }}
            >
              Reference: {error.digest}
            </p>
          ) : null}

          <button
            type="button"
            onClick={() => retry()}
            style={{
              background: '#14110f',
              color: '#f4f1ea',
              border: 'none',
              padding: '0.6rem 1.1rem',
              font: 'inherit',
              cursor: 'pointer',
            }}
          >
            Try again
          </button>
        </main>
      </body>
    </html>
  );
}