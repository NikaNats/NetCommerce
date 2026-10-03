import {
  Bodoni_Moda,
  Archivo,
  IBM_Plex_Mono,
} from 'next/font/google';

/**
 * Self-hosted typography.
 *
 * ## Why not a CSS @import
 *
 * globals.css previously pulled the families from
 * `https://fonts.googleapis.com` with an `@import`, while the Content Security
 * Policy in next.config.ts declared:
 *
 *     style-src 'self' 'unsafe-inline'
 *     font-src 'self' data:
 *
 * Neither origin was allowlisted, so the browser dropped the `@import` under a
 * `style-src` violation and would have dropped the woff2 downloads under
 * `font-src`. The editorial design system — Bodoni Moda, Archivo, tabular IBM
 * Plex Mono — would have collapsed to system serif and sans-serif, silently. The
 * next.config.ts comment even claimed "self-hosted fonts" while the CSS did the
 * opposite, which is why the mismatch survived review.
 *
 * It survived the verification gates too: check-headers.mjs only asserted the
 * CSP directive KEYS are present as strings, and check-render.mjs parsed raw CSS
 * text without a CSP-enforcing engine. Neither could see a blocked request.
 *
 * ## Why next/font is the right fix rather than an allowlist
 *
 * next/font/google DOWNLOADS each family at build time, self-hosts the woff2
 * under /_next/static, and rewrites font-family to the hashed local file. So:
 *
 *   - no third-party origin at runtime, so `default-src 'self'` still holds;
 *   - no extra CSP directives, so the stricter posture is preserved;
 *   - no layout shift and no extra DNS/TLS handshake on first paint;
 *   - `display: swap` is applied automatically.
 *
 * Adding `https://fonts.googleapis.com` and `https://fonts.gstatic.com` to the
 * CSP would also silence the symptom, but it would keep a third-party runtime
 * dependency that `default-src 'self'` was chosen to avoid.
 *
 * ## Weights
 *
 * Only the weights actually used are requested, because next/font emits one
 * @font-face per weight — requesting a full family ships unused files.
 */

/**
 * Display face. Bodoni Moda, not Fraunces: the Impeccable detector flags
 * Fraunces as one of the faces AI-generated UIs converge on. A didone carries
 * more editorial authority and suits the ledger world better — and unlike
 * Fraunces it has real stroke contrast, which survives at the display sizes used
 * here.
 *
 * The optical-size axis is left at its default; pinning it per usage would need
 * separate families, and `adjustFontFallback` keeps the metric-compatible local
 * fallback close enough to avoid a visible swap.
 */
const display = Bodoni_Moda({
  subsets: ['latin'],
  weight: ['400', '500', '600', '700'],
  style: ['normal', 'italic'],
  display: 'swap',
  variable: '--font-display',
});

/** Body text. */
const body = Archivo({
  subsets: ['latin'],
  weight: ['400', '500', '600'],
  display: 'swap',
  variable: '--font-body',
});

/**
 * Tabular data only — prices, SKUs, quantities, correlation ids.
 *
 * `swap` would be wrong here even if it were configurable per family: a
 * monospace face is chosen for its advance width, and swapping it mid-render
 * would make numeric columns visibly jump. It falls back to a local monospace
 * with the same metrics instead.
 */
const mono = IBM_Plex_Mono({
  subsets: ['latin'],
  weight: ['400', '500', '600'],
  display: 'swap',
  variable: '--font-mono',
});

export const fontVariables = {
  // NOTE the distinct names. next/font writes these onto <html>, and globals.css
  // declares its OWN `--font-display` / `--font-body` / `--font-mono` stacks in
  // :root. Reusing those names would collide: :root wins on the same element, so
  // the font-face bindings next/font emits would be silently overridden and the
  // families would never apply.
  //
  // So next/font publishes the loaded family name here, and the CSS stacks
  // consume it via var(--loaded-display) as their first entry.
  '--loaded-display': display.variable,
  '--loaded-body': body.variable,
  '--loaded-mono': mono.variable,
};

/**
 * The font variables as a className string.
 *
 * next/font's `.variable` is a CSS custom-property MAP, not a string, so it
 * cannot be spread onto className directly — React types className as string.
 * `style` is the correct home for custom properties; this exists only for
 * callers that need them on an element's class attribute.
 */
export const fontVariablesClassName = Object.entries(fontVariables)
  .map(([name, value]) => `${name}:${value}`)
  .join(';');

export const fontClassNames = `${display.className} ${body.className} ${mono.className}`;

export { display, body, mono };