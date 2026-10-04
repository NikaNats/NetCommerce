import type { Metadata } from 'next';

import { siteOrigin } from '@/lib/config';
import { fontClassNames, fontVariablesClassName } from './fonts';
import './globals.css';

export const metadata: Metadata = {
  // metadataBase makes every relative OG/canonical URL absolute. Without it
  // social unfurlers and indexers receive relative paths, which they discard.
  metadataBase: new URL(siteOrigin()),
  title: {
    default: 'NetCommerce',
    template: '%s · NetCommerce',
  },
  description: 'Catalog, basket and order tracking for NetCommerce.',
  openGraph: {
    siteName: 'NetCommerce',
    type: 'website',
  },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    // fontVariablesClassName publishes the --font-* custom properties on <html>
    // so globals.css can reference them; fontClassNames applies the hashed
    // @font-face families on <body>.
    //
    // Both come from next/font/google, which self-hosts the woff2 files at build
    // time. The previous CSS @import from fonts.googleapis.com was blocked by the
    // Content Security Policy — `style-src 'self' 'unsafe-inline'` and
    // `font-src 'self' data:` allowlisted neither origin — so the whole type system
    // silently fell back to system serif/sans. See src/app/fonts.ts.
    <html lang="en" className={fontVariablesClassName}>
      <body className={fontClassNames}>
        {/* Skip link: keyboard users land here first, not in the nav. */}
        <a className="skip-link" href="#main">
          Skip to content
        </a>

        <div className="shell">
          <div className="masthead rail--full">
            <a href="/" aria-label="NetCommerce home">
              <p className="wordmark">
                Net<em>Commerce</em>
              </p>
            </a>
            <nav className="masthead__nav" aria-label="Primary">
              <a href="/catalog">Catalog</a>
              <a href="/basket">Basket</a>
            </nav>
            <p className="field-label">Webhook-first commerce</p>
          </div>

          {children}
        </div>

        <div className="shell">
          <p className="field-label rail--full" style={{ marginTop: '3rem' }}>
            Prices, SKUs and order ids set in a tabular monospace · Contrast
            verified to WCAG AA
          </p>
        </div>
      </body>
    </html>
  );
}