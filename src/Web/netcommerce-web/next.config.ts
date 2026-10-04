import type { NextConfig } from 'next';

/**
 * The origin that serves product imagery, for the CSP `img-src` allowlist.
 *
 * The API's `CdnBaseUrl` is `http://localhost:9000/netcommerce` in development
 * (MinIO) — CLEARTEXT http. An `img-src` of `'self' data: https:` therefore
 * hard-blocks every product image locally, which is why this cannot simply be
 * loosened to `http:`.
 *
 * Returning a CSP *source expression* rather than a bare host keeps the policy
 * well-formed when the variable is unset. Two details matter:
 *
 *   - bare `host` allows only https on that host, so a cleartext MinIO origin
 *     would still be blocked. `http://host` is used deliberately for that case.
 *   - an empty result yields nothing, not a stray space, so an unset variable
 *     degrades to the previous strict posture rather than accidentally widening.
 */
function readStorageOrigin(): string {
  const raw = process.env.STORAGE_ORIGIN?.trim();
  if (!raw) return '';

  // Already scheme-qualified: use as-is.
  if (/^https?:\/\//i.test(raw)) return raw;

  // A bare host gets an explicit scheme so the development cleartext origin works.
  return `http://${raw.replace(/^\/+/, '')}`;
}

/**
 * The API origin, for the CSP `connect-src` allowlist.
 *
 * Same well-formedness rules as {@link readStorageOrigin}: an unset variable
 * contributes nothing rather than a stray space, so `connect-src` degrades to
 * `'self'` — which is the safe direction, since that is the posture this app had
 * before realtime existed.
 *
 * Both the origin itself and its ws:// equivalent are returned, because
 * `connect-src` distinguishes them: an `http://host` source does not permit a
 * `ws://host` connection, and the SignalR client uses the websocket transport.
 */
function readApiOrigin(): string {
  const raw = process.env.PUBLIC_API_ORIGIN?.trim() ?? process.env.API_BASE_URL?.trim();
  if (!raw) return '';

  if (/^https?:\/\//i.test(raw)) {
    const url = new URL(raw);
    const scheme = url.protocol === 'https:' ? 'wss:' : 'ws:';
    return `${url.origin} ${scheme}//${url.host}`;
  }

  // A bare host: assume cleartext, as the development API does.
  return `http://${raw.replace(/^\/+/, '')} ws://${raw.replace(/^\/+/, '')}`;
}

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
  // img-src must permit the configured storage origin.
  //
  // Development CdnBaseUrl is "http://localhost:9000/netcommerce" (MinIO) — CLEARTEXT
  // http. The previous `img-src 'self' data: https:` allowed only 'self', data: and
  // TLS, so the browser hard-blocked every product image during local development.
  //
  // STORAGE_ORIGIN is the allowlist rather than a blanket `http:`. `http:` would also
  // permit any cleartext origin, which weakens the policy for production as well as
  // development — and an attacker who can inject an <img src> could exfiltrate data
  // over cleartext. The allowlist keeps `default-src 'self'` meaningful.
  //
  // An empty STORAGE_ORIGIN simply omits the entry rather than emitting a stray space,
  // so an unset variable degrades to the previous strict posture instead of
  // accidentally permitting http:.
  ["img-src 'self' data: https:", readStorageOrigin()].filter(Boolean).join(' '),
  "font-src 'self' data:",

  // The realtime hub is a WEBSOCKET to the API origin, so connect-src must permit it.
  //
  // This directive is the one that would catch a regression where a client fetch
  // starts handling tokens in the browser — so widening it needs a reason, and this is
  // the reason: SignalR's negotiate/long-polling handshake plus the websocket itself
  // are cross-origin, and no same-origin alternative exists.
  //
  // A Next.js route handler CANNOT proxy this. Route handlers handle HTTP requests;
  // they do not perform the HTTP Upgrade handshake a websocket needs, and a `rewrites()`
  // entry would return a 404 for the upgrade just as it does today. The honest options
  // were (a) a reverse proxy in front of both, or (b) allow the browser to reach the
  // hub directly. (b) is chosen here and the hub carries only the session cookie —
  // never a bearer token — so a cross-origin socket grants no more than the page
  // already has.
  //
  // The API origin is allowlisted explicitly rather than using `*`: a wildcard would
  // also permit exfiltrating data to any host over websockets.
  ["connect-src 'self'", readApiOrigin()].filter(Boolean).join(' '),

  "form-action 'self'",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "object-src 'none'",
].join('; ');

const nextConfig: NextConfig = {
  output: 'standalone',

  // No `X-Powered-By: Next.js` banner. It advertises the exact framework to
  // every scanner for zero benefit.
  poweredByHeader: false,

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
    // HSTS is production-only: in development the app is plain HTTP on
    // localhost, and a cached HSTS directive would then force HTTPS against a
    // server that does not speak it. Production always terminates TLS at the
    // edge, so `includeSubDomains` is safe there.
    const extraSecurity =
      process.env.NODE_ENV === 'production'
        ? [
            {
              key: 'Strict-Transport-Security',
              value: 'max-age=63072000; includeSubDomains; preload',
            },
          ]
        : [];

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
          ...extraSecurity,
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