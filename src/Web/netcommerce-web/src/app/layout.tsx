import type { Metadata } from 'next';

import './globals.css';

export const metadata: Metadata = {
  title: 'NetCommerce',
  description: 'Catalog, basket and order tracking for NetCommerce.',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
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