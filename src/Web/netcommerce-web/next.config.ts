import type { NextConfig } from 'next';

/**
 * Content Security Policy.
 *
 * Built from the app's ACTUAL needs rather than a generic template, because a CSP
 * that breaks the app gets deleted, and one that is merely decorative is
 * theatre.
 *
 *  - default-src 'self'  — everything unlisted is denied.
 *  - script-src 'self' 'unsafe-inline' — Next injects an inline bootstrap script
 *    (`self.__next_f.push(...)` for the flight payload). Removing 'unsafe-inline'
 *    requires a per-request nonce, which this deployment does not set up. Stated
 *    plainly rather than left as a silent gap: this is the weakest directive.
 *  - style-src 'self' 'unsafe-inline' — same reason; the design system sets a few
 *    inline styles.
 *  - img-src 'self' data: https: — product imagery is served from an arbitrary
 *    CDN host, so https: rather than a per-host allowlist.
 *  - font-src 'self' data: — self-hosted fonts, with data: for the favicon.
 *  - connect-src 'self' — every API call is server-side (the BFF holds the
 *    tokens), so the browser never talks to the .NET API or Keycloak directly.
 *    This is the directive that would catch a regression where a client fetch is
 *    introduced and a token starts being handled in the browser.
 *  - form-action 'self' — the logout form posts to this origin only.
 *  - frame-ancestors 'none' — clickjacking: nothing may embed this app.
 */
const CSP = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: https:",
  "font-src 'self' data:",
  "connect-src 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "object-src 'none'",
].join('; ');

const nextConfig: NextConfig = {
  output: 'standalone',

  // NOTE: no `experimental.instrumentationHook`.
  // That flag was required only on Next 13/14. instrumentation.ts is stable
  // since Next 15, and setting the flag on 15+ emits a warning (and is a no-op
  // on 16). The original design guide set it while also specifying Next 16 —
  // the two contradict each other.
  //
  // typedRoutes is on: every href/redirect is checked against the real app
  // routes at build time instead of failing at runtime in production.

  typedRoutes: true,

  async headers() {
    return [
      {
        source: '/:path*',
        headers: [
          { key: 'Content-Security-Policy', value: CSP },
          { key: 'X-Content-Type-Options', value: 'nosniff' },
          { key: 'X-Frame-Options', value: 'DENY' },
          { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
          {
            key: 'Permissions-Policy',
            // This app needs no camera, microphone, geolocation, or payment
            // access. Denying them removes those from the attack surface.
            value: 'camera=(), microphone=(), geolocation=(), payment=()',
          },
        ],
      },
    ];
  },

  images: {
    remotePatterns: [
      { protocol: 'http', hostname: 'localhost' },
      { protocol: 'https', hostname: '*.blob.core.windows.net' },
    ],
  },
};

export default nextConfig;